"""Summarize PID-scoped PresentMon samples using the recorded QPC boundaries."""
import csv
import json
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path

root = Path(sys.argv[1])
runs = []
for sample_file in sorted(root.glob("*-sample.json")):
    label = sample_file.name.removesuffix("-sample.json")
    sample = json.loads(sample_file.read_text(encoding="utf-8-sig"))
    with (root / (label + ".csv")).open(encoding="utf-8-sig", newline="") as stream:
        rows = [r for r in csv.DictReader(stream)
                if int(r["ProcessID"]) == sample["pid"]
                and sample["startQpcMs"] <= float(r["CPUStartQPCTime"]) < sample["endQpcMs"]]
    chains = Counter(r["SwapChainAddress"] for r in rows)
    if len(chains) != 1:
        raise RuntimeError(f"Expected exactly one measured native swapchain: {label}: {chains}")
    if len(rows) < 100:
        raise RuntimeError(f"Insufficient measured frames: {label}")
    def values(column):
        return [float(r[column]) for r in rows if r[column] not in ("NA", "", "0.0000")]
    gpu = values("GPUTime")
    busy = values("GPUBusy")
    if min(len(gpu), len(busy)) < 0.95 * len(rows):
        raise RuntimeError(f"Missing GPU timing coverage: {label}")
    displayed = sum(r["DisplayedTime"] not in ("NA", "") and float(r["DisplayedTime"]) > 0 for r in rows)
    seconds = sample["sampleSeconds"]
    model_round, implementation = label.rsplit("-", 1)
    model, repeat = model_round.rsplit("-", 1)
    runs.append(dict(model=model, implementation=implementation, repeat=int(repeat),
                     seconds=seconds, presentedFrames=len(rows), displayedFrames=displayed,
                     presentedFps=len(rows)/seconds, displayedFps=displayed/seconds,
                     gpuTimeMeanMs=statistics.mean(gpu), gpuBusyMeanMs=statistics.mean(busy),
                     gpuTimeP95Ms=statistics.quantiles(gpu, n=100)[94],
                     gpuTimingCoverage=len(gpu)/len(rows),
                     syncIntervals=sorted({r["SyncInterval"] for r in rows}),
                     presentModes=sorted({r["PresentMode"] for r in rows})))
groups = defaultdict(list)
for run in runs:
    groups[(run["model"], run["implementation"])].append(run)
summary = []
for (model, implementation), members in groups.items():
    if len(members) != 3:
        raise RuntimeError(f"Expected three repeats for {model}/{implementation}, got {len(members)}")
    aggregate = dict(model=model, implementation=implementation, repeats=len(members),
                     measuredFrames=sum(r["presentedFrames"] for r in members))
    for key in ("presentedFps", "displayedFps", "gpuTimeMeanMs", "gpuBusyMeanMs", "gpuTimeP95Ms"):
        aggregate[key] = statistics.mean(r[key] for r in members)
        aggregate[key+"StdDev"] = statistics.stdev(r[key] for r in members)
    summary.append(aggregate)
result = dict(method="Equal-weight arithmetic mean of three independent run metrics; GPUTime includes GPU wait, GPUBusy is active engine work; display FPS counts frames with positive DisplayedTime", runs=runs, summary=summary)
(root / "interaction-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(summary, ensure_ascii=False, indent=2))
