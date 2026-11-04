use crate::{emit, render::context::NativeRenderer, Command, Request};
use litematica_preview_core::{MeshBatch, PreviewInfo};
use std::io::{self, BufRead, Read};
use std::ptr::{null, null_mut};
use std::sync::{mpsc, Arc};
use windows_sys::Win32::{
    Foundation::*,
    Graphics::Gdi::{BeginPaint, EndPaint, PAINTSTRUCT},
    System::{LibraryLoader::GetModuleHandleW, Threading::*},
    UI::{HiDpi::*, Input::KeyboardAndMouse::*, WindowsAndMessaging::*},
};

pub enum Work {
    Prepared([f32; 3], [f32; 3]),
    Batch(MeshBatch),
    Upload(MeshBatch, crate::render::upload::UploadPermit),
    Complete(PreviewInfo),
    Command(Command),
    Error(String),
    End,
}

struct Wake(usize);
impl Wake {
    fn signal(&self) {
        unsafe {
            SetEvent(self.0 as HANDLE);
        }
    }
}
impl Drop for Wake {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0 as HANDLE);
        }
    }
}

struct Viewport {
    renderer: NativeRenderer,
    hwnd: HWND,
    parent: HWND,
    previous: Option<(f32, f32)>,
    scale: f64,
    size: (u32, u32),
    frames: u64,
    inputs: u64,
    ready: bool,
    closed: bool,
    resize_pending: bool,
}

impl Viewport {
    fn sync_bounds(&mut self) -> Result<(), String> {
        unsafe {
            if IsWindow(self.parent) == 0 {
                self.closed = true;
                return Ok(());
            }
            let mut rect: RECT = std::mem::zeroed();
            if GetClientRect(self.parent, &mut rect) == 0 {
                return Err("Preview parent has no client rectangle".into());
            }
            let width = rect.right.max(0);
            let height = rect.bottom.max(0);
            let mut own: RECT = std::mem::zeroed();
            GetClientRect(self.hwnd, &mut own);
            if own.right != width || own.bottom != height {
                // Asynchronous positioning never waits on the launcher's UI thread.
                if SetWindowPos(
                    self.hwnd,
                    null_mut(),
                    0,
                    0,
                    width,
                    height,
                    SWP_NOACTIVATE | SWP_NOZORDER | SWP_ASYNCWINDOWPOS,
                ) == 0
                {
                    return Err("Unable to resize the native viewport".into());
                }
            }
            self.resize_pending = true;
        }
        Ok(())
    }
    fn apply_size(&mut self) {
        if !self.resize_pending {
            return;
        }
        self.resize_pending = false;
        unsafe {
            let mut rect: RECT = std::mem::zeroed();
            GetClientRect(self.hwnd, &mut rect);
            self.size = (rect.right.max(0) as u32, rect.bottom.max(0) as u32);
            self.scale = f64::from(GetDpiForWindow(self.hwnd).max(96)) / 96.;
        }
        if self.size.0 != 0 && self.size.1 != 0 {
            self.renderer.set_scale(self.scale);
            self.renderer.resize(self.size.0, self.size.1);
        }
    }
    fn visible(&self) -> bool {
        unsafe {
            self.size.0 != 0
                && self.size.1 != 0
                && IsWindowVisible(self.hwnd) != 0
                && IsIconic(GetAncestor(self.hwnd, GA_ROOT)) == 0
        }
    }
    fn present(&mut self) -> Result<(), String> {
        self.apply_size();
        if self.visible() && self.renderer.camera().dirty() {
            self.renderer.render().map_err(|e| e.to_string())?;
            self.frames += 1;
        }
        Ok(())
    }
    fn report(&self, event: &str) {
        emit(
            serde_json::json!({"event":event,"mode":"native-dx12","hwnd":self.hwnd as isize,
            "frames":self.frames,"inputs":self.inputs,"width":self.size.0,"height":self.size.1,
            "scale":self.scale,"visible":self.visible()}),
        );
    }
    fn command(&mut self, command: Command) -> Result<(), String> {
        match command {
            Command::Close => self.closed = true,
            Command::Resize => self.sync_bounds()?,
            Command::Status => {}
            Command::Stage {
                left,
                top,
                width,
                height,
            } => self.renderer.set_stage(left, top, width, height),
            Command::Fit => self.renderer.camera().fit(),
            Command::Orbit { x, y } if x.is_finite() && y.is_finite() => {
                self.renderer.mark_user_interaction();
                self.renderer
                    .camera()
                    .orbit(x.clamp(-2000., 2000.), y.clamp(-2000., 2000.));
            }
            Command::Pan { x, y } if x.is_finite() && y.is_finite() => {
                self.renderer.mark_user_interaction();
                self.renderer
                    .camera()
                    .pan(x.clamp(-2000., 2000.), y.clamp(-2000., 2000.));
            }
            Command::Zoom { factor } if factor.is_finite() => {
                self.renderer.mark_user_interaction();
                self.renderer.camera().zoom(factor.clamp(0.1, 10.));
            }
            _ => return Err("Invalid native viewport command".into()),
        }
        self.present()?;
        self.report("ack");
        Ok(())
    }
}

impl Viewport {
    unsafe fn handle_native(&mut self, message: u32, w: WPARAM, l: LPARAM) -> Option<LRESULT> {
        let hwnd = self.hwnd;
        match message {
            WM_ERASEBKGND => return Some(1),
            WM_PAINT => {
                self.renderer.camera().mark_dirty();
                return Some(0);
            }
            WM_SIZE | WM_DPICHANGED_AFTERPARENT | WM_DPICHANGED => self.resize_pending = true,
            WM_SHOWWINDOW | WM_DISPLAYCHANGE => self.renderer.camera().mark_dirty(),
            WM_LBUTTONDOWN => {
                SetFocus(hwnd);
                SetCapture(hwnd);
                self.previous = Some(((l as i16) as f32, ((l >> 16) as i16) as f32));
                return Some(0);
            }
            WM_LBUTTONUP => {
                self.previous = None;
                ReleaseCapture();
                return Some(0);
            }
            WM_CAPTURECHANGED | WM_KILLFOCUS => self.previous = None,
            WM_MOUSEMOVE if w & 1 != 0 => {
                let now = ((l as i16) as f32, ((l >> 16) as i16) as f32);
                if let Some(previous) = self.previous {
                    let dx = (now.0 - previous.0) / self.scale as f32;
                    let dy = (now.1 - previous.1) / self.scale as f32;
                    self.renderer.mark_user_interaction();
                    if w & 4 != 0 {
                        self.renderer.camera().pan(dx, dy);
                    } else {
                        self.renderer.camera().orbit(dx, dy);
                    }
                    self.inputs += 1;
                }
                self.previous = Some(now);
                return Some(0);
            }
            WM_MOUSEWHEEL => {
                self.renderer.mark_user_interaction();
                self.renderer
                    .camera()
                    .zoom_wheel(-((w >> 16) as i16) as f32, 0);
                self.inputs += 1;
                return Some(0);
            }
            WM_KEYDOWN => {
                let shift = GetKeyState(VK_SHIFT as i32) < 0;
                self.renderer.mark_user_interaction();
                match w as u16 {
                    VK_LEFT => self.renderer.camera().arrow(-20., 0., shift),
                    VK_RIGHT => self.renderer.camera().arrow(20., 0., shift),
                    VK_UP => self.renderer.camera().arrow(0., -20., shift),
                    VK_DOWN => self.renderer.camera().arrow(0., 20., shift),
                    VK_HOME | 0x52 => self.renderer.camera().fit(),
                    VK_ADD | VK_OEM_PLUS => self.renderer.camera().zoom(0.85),
                    VK_SUBTRACT | VK_OEM_MINUS => self.renderer.camera().zoom(1.18),
                    VK_TAB => {
                        PostMessageW(self.parent, WM_APP + 2, shift as usize, 0);
                        return Some(0);
                    }
                    _ => return None,
                }
                self.inputs += 1;
                return Some(0);
            }
            WM_GETDLGCODE => {
                return Some((DLGC_WANTARROWS | DLGC_WANTCHARS | DLGC_WANTTAB) as isize)
            }
            WM_DESTROY => {
                self.closed = true;
                return Some(0);
            }
            _ => {}
        }
        None
    }
}

struct WindowEvents {
    queue: std::collections::VecDeque<(u32, WPARAM, LPARAM)>,
}
unsafe extern "system" fn window_proc(hwnd: HWND, message: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    if message == WM_ERASEBKGND {
        return 1;
    }
    if message == WM_GETDLGCODE {
        return (DLGC_WANTARROWS | DLGC_WANTCHARS | DLGC_WANTTAB) as isize;
    }
    if message == WM_PAINT {
        let mut paint: PAINTSTRUCT = std::mem::zeroed();
        BeginPaint(hwnd, &mut paint);
        EndPaint(hwnd, &paint);
    }
    let pointer = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut WindowEvents;
    if !pointer.is_null()
        && matches!(
            message,
            WM_PAINT
                | WM_SIZE
                | WM_DPICHANGED
                | WM_DPICHANGED_AFTERPARENT
                | WM_SHOWWINDOW
                | WM_DISPLAYCHANGE
                | WM_LBUTTONDOWN
                | WM_LBUTTONUP
                | WM_CAPTURECHANGED
                | WM_KILLFOCUS
                | WM_MOUSEMOVE
                | WM_MOUSEWHEEL
                | WM_KEYDOWN
                | WM_DESTROY
        )
    {
        let queue = &mut (*pointer).queue;
        if message == WM_MOUSEMOVE && queue.back().is_some_and(|e| e.0 == message && e.1 == w) {
            queue.pop_back();
        }
        if queue.len() < 4096 {
            queue.push_back((message, w, l));
        }
        return 0;
    }
    if message == WM_PAINT {
        return 0;
    }
    DefWindowProcW(hwnd, message, w, l)
}

struct Window(HWND);
impl Drop for Window {
    fn drop(&mut self) {
        unsafe {
            SetWindowLongPtrW(self.0, GWLP_USERDATA, 0);
            DestroyWindow(self.0);
        }
    }
}

pub fn run(request: Request) -> Result<(), String> {
    let parent = request.parent as HWND;
    unsafe {
        let mut owner = 0;
        if IsWindow(parent) == 0
            || GetWindowThreadProcessId(parent, &mut owner) == 0
            || owner != request.parent_process
        {
            return Err("Invalid launcher viewport parent".into());
        }
        let context = GetWindowDpiAwarenessContext(parent);
        if context.is_null() || SetThreadDpiAwarenessContext(context).is_null() {
            return Err("Cannot match launcher DPI context".into());
        }
    }
    let class: Vec<u16> = "CMUmclNativeSchematicViewport\0".encode_utf16().collect();
    let window = unsafe {
        let instance = GetModuleHandleW(null());
        let definition = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(window_proc),
            hInstance: instance,
            lpszClassName: class.as_ptr(),
            hCursor: LoadCursorW(null_mut(), IDC_ARROW),
            ..std::mem::zeroed()
        };
        if RegisterClassW(&definition) == 0 {
            return Err("Unable to register native viewport".into());
        }
        let mut rect: RECT = std::mem::zeroed();
        GetClientRect(parent, &mut rect);
        let handle = CreateWindowExW(
            WS_EX_NOPARENTNOTIFY,
            class.as_ptr(),
            class.as_ptr(),
            WS_CHILD | WS_CLIPSIBLINGS,
            0,
            0,
            rect.right.max(1),
            rect.bottom.max(1),
            parent,
            null_mut(),
            instance,
            null(),
        );
        if handle.is_null() {
            return Err(format!(
                "Unable to create native viewport: {}",
                GetLastError()
            ));
        }
        Window(handle)
    };
    let renderer =
        NativeRenderer::new_from_hwnd(window.0 as isize, (1, 1)).map_err(|e| e.to_string())?;
    let mut view = Box::new(Viewport {
        renderer,
        hwnd: window.0,
        parent,
        previous: None,
        scale: 1.,
        size: (1, 1),
        frames: 0,
        inputs: 0,
        ready: false,
        closed: false,
        resize_pending: true,
    });
    view.renderer.begin_load();
    view.apply_size();
    view.renderer.render().map_err(|e| e.to_string())?;
    view.frames = 1;
    unsafe {
        ShowWindow(window.0, SW_SHOWNOACTIVATE);
    }
    let mut events = Box::new(WindowEvents {
        queue: Default::default(),
    });
    let pointer = &mut *events as *mut WindowEvents;
    unsafe {
        SetWindowLongPtrW(window.0, GWLP_USERDATA, pointer as isize);
    }
    let result = event_loop(&request, &mut view, pointer);
    unsafe {
        SetWindowLongPtrW(window.0, GWLP_USERDATA, 0);
    }
    // The swapchain must be released before its native window is destroyed.
    drop(view);
    drop(window);
    result
}

fn event_loop(
    request: &Request,
    view: &mut Viewport,
    events: *mut WindowEvents,
) -> Result<(), String> {
    let handle = unsafe { CreateEventW(null(), 0, 0, null()) };
    if handle.is_null() {
        return Err("Unable to create viewport wake event".into());
    }
    let wake = Arc::new(Wake(handle as usize));
    let (sender, receiver) = mpsc::sync_channel(2);
    let upload = view
        .renderer
        .upload_window(usize::from(request.threads.unwrap_or(1)));
    let loader_upload = upload.clone();
    let loader_sender = sender.clone();
    let loader_wake = wake.clone();
    let atlas_side = view.renderer.max_texture_side().min(8192);
    let load_request = Request {
        input: request.input.clone(),
        pack: request.pack.clone(),
        parent: request.parent,
        parent_process: request.parent_process,
        threads: request.threads,
    };
    std::thread::Builder::new()
        .name("schematic-mesh".into())
        .spawn(move || {
            let send = |work| {
                let work = match work {
                    Work::Batch(batch) => {
                        let bytes = batch.geometry.len()
                            + batch.textures.iter().map(|t| t.pixels.len()).sum::<usize>();
                        Work::Upload(batch, loader_upload.acquire(bytes, &|| Ok(()))?)
                    }
                    Work::Complete(info) => {
                        loader_upload.drain(&|| Ok(()))?;
                        Work::Complete(info)
                    }
                    other => other,
                };
                loader_sender
                    .send(work)
                    .map_err(|_| "Viewport closed".to_owned())?;
                loader_wake.signal();
                Ok(())
            };
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                crate::load(load_request, atlas_side, send)
            }));
            let error = match result {
                Ok(Ok(())) => None,
                Ok(Err(error)) => Some(error),
                Err(_) => Some("Preview decoder panicked".into()),
            };
            if let Some(error) = error {
                let _ = loader_sender.send(Work::Error(error));
                loader_wake.signal();
            }
        })
        .map_err(|e| e.to_string())?;
    let input_wake = wake.clone();
    std::thread::Builder::new()
        .name("schematic-control".into())
        .spawn(move || {
            let stdin = io::stdin();
            let mut input = stdin.lock();
            loop {
                let mut line = String::new();
                let work = match input.by_ref().take(4097).read_line(&mut line) {
                    Ok(0) => Work::End,
                    Ok(n) if n <= 4096 => match serde_json::from_str(&line) {
                        Ok(command) => Work::Command(command),
                        Err(error) => Work::Error(error.to_string()),
                    },
                    _ => Work::Error("Invalid control message".into()),
                };
                let end = matches!(work, Work::End | Work::Error(_));
                if sender.send(work).is_err() {
                    break;
                }
                input_wake.signal();
                if end {
                    break;
                }
            }
        })
        .map_err(|e| e.to_string())?;
    while !view.closed {
        unsafe {
            let mut message: MSG = std::mem::zeroed();
            let mut count = 0;
            while count < 128 && PeekMessageW(&mut message, null_mut(), 0, 0, PM_REMOVE) != 0 {
                if message.message == WM_QUIT {
                    view.closed = true;
                    break;
                }
                TranslateMessage(&message);
                DispatchMessageW(&message);
                count += 1;
            }
            if IsWindow(view.parent) == 0 || IsWindow(view.hwnd) == 0 {
                break;
            }
        }
        let pending = unsafe { std::mem::take(&mut (*events).queue) };
        let refresh = !pending.is_empty() || view.resize_pending;
        for (message, w, l) in pending {
            unsafe {
                view.handle_native(message, w, l);
            }
        }
        if view.closed {
            break;
        }
        view.apply_size();
        if refresh {
            view.present()?;
        }
        for _ in 0..8 {
            let Ok(work) = receiver.try_recv() else {
                break;
            };
            match work {
                Work::Prepared(min, max) => view.renderer.prepare_load(min, max),
                Work::Upload(batch, permit) => {
                    // Preserve the upstream progressive upload and first-visible-frame policy.
                    view.renderer
                        .attach_batch(&batch, permit)
                        .map_err(|e| e.to_string())?;
                }
                Work::Complete(info) => {
                    view.renderer.finish_load(&info);
                    view.renderer.camera().mark_dirty();
                    view.present()?;
                    view.ready = true;
                    emit(
                        serde_json::json!({"event":"ready","mode":"native-dx12","hwnd":view.hwnd as isize,
                        "blocks":info.block_count,"triangles":info.triangle_count,"frames":view.frames,
                        "inputs":view.inputs,"width":view.size.0,"height":view.size.1,"scale":view.scale,"visible":view.visible()}),
                    );
                }
                Work::Command(command) => view.command(command)?,
                Work::Error(error) => return Err(error),
                Work::End => view.closed = true,
                Work::Batch(_) => return Err("Unadmitted GPU batch".into()),
            }
        }
        if unsafe { !(*events).queue.is_empty() } {
            continue;
        }
        if !view.closed {
            unsafe {
                if MsgWaitForMultipleObjectsEx(
                    1,
                    &handle,
                    INFINITE,
                    QS_ALLINPUT,
                    MWMO_INPUTAVAILABLE,
                ) == WAIT_FAILED
                {
                    return Err("Native viewport wait failed".into());
                }
            }
        }
    }
    Ok(())
}
