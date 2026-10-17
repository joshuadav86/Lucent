// The main thread and the plugin's window on Windows.
//
// Work from the thread that talks to Lucent arrives as messages to a hidden window, so it still runs
// while a plugin shows a menu or dialog of its own (those run their own message loops). The plugin's
// window sits above Lucent's editor (its owner), hides rather than closes when its X is clicked, and is
// sized in pixels for the screen it's on, as VST3 plugins expect on Windows.

#include "plugin.h"
#include "ui.h"

#include "pluginterfaces/gui/iplugview.h"
#include "pluginterfaces/gui/iplugviewcontentscalesupport.h"
#include "pluginterfaces/base/funknownimpl.h"

#include <windows.h>
#include <ole2.h>

#include <algorithm>
#include <exception>
#include <stdexcept>
#include <string>

using namespace Steinberg;

namespace Lucent {

namespace {

constexpr UINT runTaskMessage = WM_APP + 1;
constexpr UINT stopMessage = WM_APP + 2;
constexpr wchar_t taskWindowClass[] = L"LucentPluginHostTasks";
constexpr wchar_t editorWindowClass[] = L"LucentPluginWindow";

HWND taskWindow = nullptr;

struct Task
{
	const std::function<void ()>* work;
	std::exception_ptr error;
	HANDLE done;
};

std::wstring widen (const std::string& text)
{
	if (text.empty ())
		return {};
	auto size = MultiByteToWideChar (CP_UTF8, 0, text.data (), static_cast<int> (text.size ()), nullptr, 0);
	std::wstring wide (static_cast<size_t> (size), L'\0');
	MultiByteToWideChar (CP_UTF8, 0, text.data (), static_cast<int> (text.size ()), wide.data (), size);
	return wide;
}

// ---- Per-monitor DPI, looked up at run time so older Windows 10 versions still start ----

UINT dpiFor (HWND window)
{
	using GetDpiForWindowFn = UINT (WINAPI*) (HWND);
	static auto getDpi = reinterpret_cast<GetDpiForWindowFn> (
	    reinterpret_cast<void*> (GetProcAddress (GetModuleHandleW (L"user32.dll"), "GetDpiForWindow")));
	auto dpi = getDpi ? getDpi (window) : 0;
	return dpi ? dpi : 96;
}

RECT windowRectForClient (int width, int height, DWORD style, UINT dpi)
{
	using AdjustForDpiFn = BOOL (WINAPI*) (LPRECT, DWORD, BOOL, DWORD, UINT);
	static auto adjust = reinterpret_cast<AdjustForDpiFn> (
	    reinterpret_cast<void*> (GetProcAddress (GetModuleHandleW (L"user32.dll"), "AdjustWindowRectExForDpi")));
	RECT rect {0, 0, width, height};
	if (adjust)
		adjust (&rect, style, FALSE, 0, dpi);
	else
		AdjustWindowRectEx (&rect, style, FALSE, 0);
	return rect;
}

// ---- The plugin's window ----

class EditorFrame : public IPlugFrame
{
public:
	tresult PLUGIN_API resizeView (IPlugView* requester, ViewRect* newSize) override
	{
		if (!requester || requester != view || !newSize || !window)
			return kInvalidArgument;
		setClientSize (newSize->getWidth (), newSize->getHeight ());
		ViewRect current;
		if (view->getSize (&current) != kResultTrue || current.getWidth () != newSize->getWidth () ||
		    current.getHeight () != newSize->getHeight ())
		{
			view->onSize (newSize);
		}
		return kResultTrue;
	}

	tresult PLUGIN_API queryInterface (const TUID iid, void** obj) override
	{
		if (FUnknownPrivate::iidEqual (iid, IPlugFrame::iid) || FUnknownPrivate::iidEqual (iid, FUnknown::iid))
		{
			*obj = static_cast<IPlugFrame*> (this);
			return kResultOk;
		}
		*obj = nullptr;
		return kNoInterface;
	}
	// One for the life of the process; plugins don't own it.
	uint32 PLUGIN_API addRef () override { return 1000; }
	uint32 PLUGIN_API release () override { return 1000; }

	void setClientSize (int width, int height)
	{
		auto style = static_cast<DWORD> (GetWindowLongPtrW (window, GWL_STYLE));
		auto rect = windowRectForClient (width, height, style, dpiFor (window));
		settingSize = true;
		SetWindowPos (window, nullptr, 0, 0, rect.right - rect.left, rect.bottom - rect.top,
		              SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
		settingSize = false;
	}

	/// Lets the plugin go before its window does.
	void detachView ()
	{
		if (!view)
			return;
		view->setFrame (nullptr);
		view->removed ();
		view = nullptr;
	}

	HWND window {nullptr};
	IPtr<IPlugView> view;
	bool resizable {false};
	bool settingSize {false};
};

EditorFrame frame;

void setContentScale (UINT dpi)
{
	if (auto scaling = U::cast<IPlugViewContentScaleSupport> (frame.view))
		scaling->setContentScaleFactor (static_cast<IPlugViewContentScaleSupport::ScaleFactor> (dpi) / 96.f);
}

LRESULT CALLBACK editorWindowProc (HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
	switch (message)
	{
		case WM_CLOSE:
			// Hidden, not closed: Lucent's "Show its window" brings it back as it was.
			ShowWindow (window, SW_HIDE);
			return 0;
		case WM_SIZE:
			if (frame.view && frame.resizable && !frame.settingSize && wParam != SIZE_MINIMIZED)
			{
				ViewRect size {0, 0, LOWORD (lParam), HIWORD (lParam)};
				frame.view->checkSizeConstraint (&size);
				frame.view->onSize (&size);
			}
			return 0;
		case WM_DPICHANGED:
		{
			setContentScale (HIWORD (wParam));
			auto suggested = reinterpret_cast<const RECT*> (lParam);
			SetWindowPos (window, nullptr, suggested->left, suggested->top, suggested->right - suggested->left,
			              suggested->bottom - suggested->top, SWP_NOZORDER | SWP_NOACTIVATE);
			return 0;
		}
		case WM_DESTROY:
			// Also reached when Lucent's window (the owner) goes first and takes this one with it.
			frame.detachView ();
			frame.window = nullptr;
			return 0;
		default:
			return DefWindowProcW (window, message, wParam, lParam);
	}
}

LRESULT CALLBACK taskWindowProc (HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
	if (message == runTaskMessage)
	{
		auto task = reinterpret_cast<Task*> (lParam);
		try
		{
			(*task->work) ();
		}
		catch (...)
		{
			task->error = std::current_exception ();
		}
		SetEvent (task->done);
		return 0;
	}
	if (message == stopMessage)
	{
		PostQuitMessage (0);
		return 0;
	}
	return DefWindowProcW (window, message, wParam, lParam);
}

} // namespace

// ---- The main thread ----

void prepareUiThread ()
{
	// Plugins draw at the screen's real resolution rather than being stretched by Windows.
	using SetAwarenessFn = BOOL (WINAPI*) (HANDLE);
	auto setAwareness = reinterpret_cast<SetAwarenessFn> (
	    reinterpret_cast<void*> (GetProcAddress (GetModuleHandleW (L"user32.dll"), "SetProcessDpiAwarenessContext")));
	if (setAwareness)
		setAwareness (reinterpret_cast<HANDLE> (static_cast<intptr_t> (-4))); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

	// Some plugin windows use drag and drop or the clipboard.
	OleInitialize (nullptr);

	auto instance = GetModuleHandleW (nullptr);
	WNDCLASSEXW tasks {sizeof (tasks)};
	tasks.lpfnWndProc = taskWindowProc;
	tasks.hInstance = instance;
	tasks.lpszClassName = taskWindowClass;
	RegisterClassExW (&tasks);
	taskWindow = CreateWindowExW (0, taskWindowClass, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance, nullptr);

	WNDCLASSEXW editor {sizeof (editor)};
	editor.lpfnWndProc = editorWindowProc;
	editor.hInstance = instance;
	editor.hCursor = LoadCursor (nullptr, IDC_ARROW);
	editor.hbrBackground = reinterpret_cast<HBRUSH> (GetStockObject (BLACK_BRUSH));
	editor.hIcon = LoadIcon (nullptr, IDI_APPLICATION);
	editor.lpszClassName = editorWindowClass;
	RegisterClassExW (&editor);
}

void runUiLoop ()
{
	MSG message;
	while (GetMessageW (&message, nullptr, 0, 0) > 0)
	{
		TranslateMessage (&message);
		DispatchMessageW (&message);
	}
	OleUninitialize ();
}

void stopUiLoop ()
{
	PostMessageW (taskWindow, stopMessage, 0, 0);
}

void runOnUiThread (const std::function<void ()>& work)
{
	Task task {&work, nullptr, CreateEventW (nullptr, TRUE, FALSE, nullptr)};
	if (!task.done || !PostMessageW (taskWindow, runTaskMessage, 0, reinterpret_cast<LPARAM> (&task)))
	{
		if (task.done)
			CloseHandle (task.done);
		throw std::runtime_error ("The plugin host's main thread isn't running.");
	}
	WaitForSingleObject (task.done, INFINITE);
	CloseHandle (task.done);
	if (task.error)
		std::rethrow_exception (task.error);
}

// ---- The plugin's window ----

void showEditor (Plugin& plugin, int64_t owner, const std::string& title)
{
	auto ownerWindow = reinterpret_cast<HWND> (static_cast<intptr_t> (owner));
	if (ownerWindow && !IsWindow (ownerWindow))
		ownerWindow = nullptr;

	if (frame.window)
	{
		SetWindowTextW (frame.window, widen (title).c_str ());
		ShowWindow (frame.window, IsIconic (frame.window) ? SW_RESTORE : SW_SHOW);
		SetForegroundWindow (frame.window);
		return;
	}

	auto controller = plugin.controller ();
	if (!controller)
		throw std::runtime_error (plugin.name () + " has no window of its own.");
	frame.view = owned (controller->createView (Vst::ViewType::kEditor));
	if (!frame.view)
		throw std::runtime_error (plugin.name () + " has no window of its own.");
	if (frame.view->isPlatformTypeSupported (kPlatformTypeHWND) != kResultTrue)
	{
		frame.view = nullptr;
		throw std::runtime_error (plugin.name () + "'s window doesn't work on Windows.");
	}

	frame.resizable = frame.view->canResize () == kResultTrue;
	DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN;
	if (frame.resizable)
		style |= WS_THICKFRAME | WS_MAXIMIZEBOX;
	frame.window = CreateWindowExW (0, editorWindowClass, widen (title).c_str (), style, CW_USEDEFAULT, CW_USEDEFAULT, 400,
	                                300, ownerWindow, nullptr, GetModuleHandleW (nullptr), nullptr);
	if (!frame.window)
	{
		frame.view = nullptr;
		throw std::runtime_error ("The window for " + plugin.name () + " couldn't be made.");
	}

	setContentScale (dpiFor (frame.window));
	frame.view->setFrame (&frame);
	if (frame.view->attached (frame.window, kPlatformTypeHWND) != kResultTrue)
	{
		frame.view->setFrame (nullptr);
		frame.view = nullptr;
		DestroyWindow (frame.window);
		throw std::runtime_error (plugin.name () + " couldn't open its window.");
	}

	ViewRect size;
	if (frame.view->getSize (&size) == kResultTrue && size.getWidth () > 0 && size.getHeight () > 0)
		frame.setClientSize (size.getWidth (), size.getHeight ());

	// Centred over Lucent's editor, kept on the screen.
	RECT window, area;
	GetWindowRect (frame.window, &window);
	auto width = window.right - window.left;
	auto height = window.bottom - window.top;
	auto anchor = ownerWindow ? ownerWindow : frame.window;
	RECT ownerRect;
	GetWindowRect (anchor, &ownerRect);
	MONITORINFO monitor {sizeof (monitor)};
	GetMonitorInfoW (MonitorFromWindow (anchor, MONITOR_DEFAULTTONEAREST), &monitor);
	area = monitor.rcWork;
	auto left = (ownerRect.left + ownerRect.right - width) / 2;
	auto top = (ownerRect.top + ownerRect.bottom - height) / 2;
	left = std::max (area.left, std::min (left, area.right - width));
	top = std::max (area.top, std::min (top, area.bottom - height));
	SetWindowPos (frame.window, nullptr, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

	ShowWindow (frame.window, SW_SHOW);
	SetForegroundWindow (frame.window);
}

void hideEditor ()
{
	if (frame.window)
		ShowWindow (frame.window, SW_HIDE);
}

void closeEditor ()
{
	if (frame.window)
		DestroyWindow (frame.window);
	frame.detachView ();
	frame.window = nullptr;
}

} // namespace Lucent
