"""Make a larger local fixture from the upstream Cottage using nbtlib 2.0.4."""
import copy
import sys
import nbtlib

source = nbtlib.load(sys.argv[1])
original = source["Regions"]["Main"]
regions = nbtlib.Compound()
for z in range(20):
    for x in range(20):
        region = copy.deepcopy(original)
        region["Position"]["x"] = nbtlib.Int(int(region["Position"]["x"]) + 18*x)
        region["Position"]["z"] = nbtlib.Int(int(region["Position"]["z"]) + 16*z)
        regions[f"Cottage_{x}_{z}"] = region
source["Regions"] = regions
metadata = source["Metadata"]
metadata["Name"] = nbtlib.String("Cottage 20x20 benchmark")
metadata["Description"] = nbtlib.String("400 tiled upstream Cottage fixtures; local performance test only")
metadata["RegionCount"] = nbtlib.Int(400)
metadata["TotalBlocks"] = nbtlib.Int(240000)
metadata["TotalVolume"] = nbtlib.Int(15*11*13*400)
metadata["EnclosingSize"] = nbtlib.Compound(x=nbtlib.Int(357), y=nbtlib.Int(11), z=nbtlib.Int(317))
source.save(sys.argv[2], gzipped=True)
