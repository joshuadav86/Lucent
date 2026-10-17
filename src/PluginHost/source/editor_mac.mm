// The main thread and the plugin's window on the Mac.
//
// The main thread runs NSApplication's loop; work from the thread that talks to Lucent is sent to it
// through the main dispatch queue, which keeps running while a plugin shows a menu or dialog of its own.
// The helper has no Dock icon or menu bar of its own (an "accessory" app). The plugin's window floats
// above Lucent's editor while Lucent (or the helper) is the frontmost app, much as an owned window does on
// Windows, and hides rather than closes when its close button is clicked. VST3 plugins on the Mac draw
// into an NSView and size themselves in points, so there's no scaling to pass on.

#include "plugin.h"
#include "ui.h"

#include "pluginterfaces/gui/iplugview.h"
#include "pluginterfaces/base/funknownimpl.h"

#import <Cocoa/Cocoa.h>
#import <CoreGraphics/CoreGraphics.h>

#include <unistd.h>

#include <algorithm>
#include <exception>
#include <stdexcept>
#include <string>

#if !__has_feature(objc_arc)
#error editor_mac.mm needs automatic reference counting (-fobjc-arc)
#endif

using namespace Steinberg;

namespace Lucent {
class EditorFrame;
}

/// Hides the plugin's window instead of closing it, and passes size changes the user makes to the plugin.
@interface LucentEditorWindowDelegate : NSObject <NSWindowDelegate>
@property (nonatomic, assign) Lucent::EditorFrame* frame;
@end

namespace Lucent {

namespace {

NSString* toNSString (const std::string& text)
{
	return [[NSString alloc] initWithBytes:text.data () length:text.size () encoding:NSUTF8StringEncoding] ?: @"";
}

/// Where a window of another app (Lucent's editor) is, in Cocoa's screen coordinates, and whose it is.
bool findWindow (int64_t number, NSRect& bounds, pid_t& owner)
{
	if (number <= 0)
		return false;
	CFArrayRef list = CGWindowListCopyWindowInfo (kCGWindowListOptionIncludingWindow, static_cast<CGWindowID> (number));
	if (!list)
		return false;
	bool found = false;
	if (CFArrayGetCount (list) > 0)
	{
		auto info = static_cast<CFDictionaryRef> (CFArrayGetValueAtIndex (list, 0));
		auto boundsInfo = static_cast<CFDictionaryRef> (CFDictionaryGetValue (info, kCGWindowBounds));
		auto pidInfo = static_cast<CFNumberRef> (CFDictionaryGetValue (info, kCGWindowOwnerPID));
		CGRect rect;
		int pid = 0;
		if (boundsInfo && CGRectMakeWithDictionaryRepresentation (boundsInfo, &rect) && pidInfo &&
		    CFNumberGetValue (pidInfo, kCFNumberIntType, &pid))
		{
			// The window server measures from the top of the main screen; Cocoa from the bottom.
			auto mainHeight = NSMaxY ([NSScreen screens].firstObject.frame);
			bounds = NSMakeRect (rect.origin.x, mainHeight - rect.origin.y - rect.size.height, rect.size.width,
			                     rect.size.height);
			owner = pid;
			found = true;
		}
	}
	CFRelease (list);
	return found;
}

} // namespace

// ---- The plugin's window ----

class EditorFrame : public IPlugFrame
{
public:
	tresult PLUGIN_API resizeView (IPlugView* requester, ViewRect* newSize) override
	{
		if (!requester || requester != view || !newSize || !window)
			return kInvalidArgument;
		setContentSize (newSize->getWidth (), newSize->getHeight ());
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

	void setContentSize (int width, int height)
	{
		settingSize = true;
		// Grows downwards, like a Windows window: the title bar stays where it is.
		auto frameRect = window.frame;
		auto content = [window frameRectForContentRect:NSMakeRect (0, 0, width, height)];
		frameRect.origin.y += frameRect.size.height - content.size.height;
		frameRect.size = content.size;
		[window setFrame:frameRect display:YES];
		settingSize = false;
	}

	/// The user resized the window: tells a resizable plugin its new size.
	void windowResized ()
	{
		if (!view || !resizable || settingSize)
			return;
		auto bounds = window.contentView.bounds;
		ViewRect size {0, 0, static_cast<int32> (bounds.size.width), static_cast<int32> (bounds.size.height)};
		view->checkSizeConstraint (&size);
		view->onSize (&size);
	}

	/// Floats above other windows while Lucent or this helper is the app in front, like a window
	/// owned by Lucent's editor; behind other apps' windows otherwise.
	void followActiveApp (pid_t active)
	{
		if (!window)
			return;
		auto above = ownerProcess != 0 && (active == ownerProcess || active == getpid ());
		window.level = above ? NSFloatingWindowLevel : NSNormalWindowLevel;
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

	NSWindow* window {nil};
	LucentEditorWindowDelegate* delegate {nil};
	id activationObserver {nil};
	IPtr<IPlugView> view;
	pid_t ownerProcess {0};
	bool resizable {false};
	bool settingSize {false};
};

namespace {

EditorFrame frame;

} // namespace

} // namespace Lucent

@implementation LucentEditorWindowDelegate

- (BOOL)windowShouldClose:(NSWindow*)sender
{
	// Hidden, not closed: Lucent's "Show its window" brings it back as it was.
	[sender orderOut:nil];
	return NO;
}

- (void)windowDidResize:(NSNotification*)notification
{
	if (self.frame)
		self.frame->windowResized ();
}

@end

namespace Lucent {

// ---- The main thread ----

void prepareUiThread ()
{
	@autoreleasepool
	{
		[NSApplication sharedApplication];
		// No Dock icon or menu bar: the plugin's window belongs with Lucent.
		[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
		[NSApp finishLaunching];
	}
}

void runUiLoop ()
{
	@autoreleasepool
	{
		[NSApp run];
	}
}

void stopUiLoop ()
{
	dispatch_async (dispatch_get_main_queue (), ^{
		[NSApp stop:nil];
		// -stop: takes effect once the loop has handled an event, so give it one.
		auto wake = [NSEvent otherEventWithType:NSEventTypeApplicationDefined
		                               location:NSZeroPoint
		                          modifierFlags:0
		                              timestamp:0
		                           windowNumber:0
		                                context:nil
		                                subtype:0
		                                  data1:0
		                                  data2:0];
		[NSApp postEvent:wake atStart:YES];
	});
}

void runOnUiThread (const std::function<void ()>& work)
{
	std::exception_ptr error;
	auto run = [&] {
		@autoreleasepool
		{
			try
			{
				work ();
			}
			catch (...)
			{
				error = std::current_exception ();
			}
		}
	};
	if ([NSThread isMainThread])
		run ();
	else
		dispatch_sync (dispatch_get_main_queue (), ^{ run (); });
	if (error)
		std::rethrow_exception (error);
}

// ---- The plugin's window ----

void showEditor (Plugin& plugin, int64_t owner, const std::string& title)
{
	NSRect ownerBounds = NSZeroRect;
	pid_t ownerProcess = 0;
	auto hasOwner = findWindow (owner, ownerBounds, ownerProcess);

	if (frame.window)
	{
		frame.window.title = toNSString (title);
		if (hasOwner)
			frame.ownerProcess = ownerProcess;
		if (frame.window.miniaturized)
			[frame.window deminiaturize:nil];
		[NSApp activateIgnoringOtherApps:YES];
		[frame.window makeKeyAndOrderFront:nil];
		frame.followActiveApp (getpid ());
		return;
	}

	auto controller = plugin.controller ();
	if (!controller)
		throw std::runtime_error (plugin.name () + " has no window of its own.");
	frame.view = owned (controller->createView (Vst::ViewType::kEditor));
	if (!frame.view)
		throw std::runtime_error (plugin.name () + " has no window of its own.");
	if (frame.view->isPlatformTypeSupported (kPlatformTypeNSView) != kResultTrue)
	{
		frame.view = nullptr;
		throw std::runtime_error (plugin.name () + "'s window doesn't work on the Mac.");
	}

	frame.resizable = frame.view->canResize () == kResultTrue;
	NSWindowStyleMask style = NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable;
	if (frame.resizable)
		style |= NSWindowStyleMaskResizable;
	frame.window = [[NSWindow alloc] initWithContentRect:NSMakeRect (0, 0, 400, 300)
	                                           styleMask:style
	                                             backing:NSBackingStoreBuffered
	                                               defer:NO];
	if (!frame.window)
	{
		frame.view = nullptr;
		throw std::runtime_error ("The window for " + plugin.name () + " couldn't be made.");
	}

	frame.window.releasedWhenClosed = NO;
	frame.window.title = toNSString (title);
	frame.window.backgroundColor = NSColor.blackColor;
	frame.window.collectionBehavior = NSWindowCollectionBehaviorFullScreenAuxiliary;
	frame.window.contentView = [[NSView alloc] initWithFrame:NSMakeRect (0, 0, 400, 300)];
	frame.delegate = [LucentEditorWindowDelegate new];
	frame.delegate.frame = &frame;
	frame.window.delegate = frame.delegate;
	frame.ownerProcess = hasOwner ? ownerProcess : 0;

	frame.view->setFrame (&frame);
	if (frame.view->attached ((__bridge void*)frame.window.contentView, kPlatformTypeNSView) != kResultTrue)
	{
		frame.view->setFrame (nullptr);
		frame.view = nullptr;
		frame.window.delegate = nil;
		[frame.window close];
		frame.window = nil;
		frame.delegate = nil;
		throw std::runtime_error (plugin.name () + " couldn't open its window.");
	}

	ViewRect size;
	if (frame.view->getSize (&size) == kResultTrue && size.getWidth () > 0 && size.getHeight () > 0)
		frame.setContentSize (size.getWidth (), size.getHeight ());

	// Centred over Lucent's editor, kept on its screen.
	auto windowRect = frame.window.frame;
	NSScreen* screen = NSScreen.mainScreen;
	if (hasOwner)
	{
		for (NSScreen* candidate in NSScreen.screens)
		{
			if (NSPointInRect (NSMakePoint (NSMidX (ownerBounds), NSMidY (ownerBounds)), candidate.frame))
				screen = candidate;
		}
		windowRect.origin.x = NSMidX (ownerBounds) - windowRect.size.width / 2;
		windowRect.origin.y = NSMidY (ownerBounds) - windowRect.size.height / 2;
		auto area = screen.visibleFrame;
		windowRect.origin.x = std::max (NSMinX (area), std::min (windowRect.origin.x, NSMaxX (area) - windowRect.size.width));
		windowRect.origin.y = std::max (NSMinY (area), std::min (windowRect.origin.y, NSMaxY (area) - windowRect.size.height));
		[frame.window setFrameOrigin:windowRect.origin];
	}
	else
	{
		[frame.window center];
	}

	// Keeps it above Lucent's editor while Lucent is in front.
	frame.activationObserver = [NSWorkspace.sharedWorkspace.notificationCenter
	    addObserverForName:NSWorkspaceDidActivateApplicationNotification
	                object:nil
	                 queue:NSOperationQueue.mainQueue
	            usingBlock:^(NSNotification* note) {
		            NSRunningApplication* app = note.userInfo[NSWorkspaceApplicationKey];
		            frame.followActiveApp (app ? app.processIdentifier : 0);
	            }];

	[NSApp activateIgnoringOtherApps:YES];
	[frame.window makeKeyAndOrderFront:nil];
	frame.followActiveApp (getpid ());
}

void hideEditor ()
{
	if (frame.window)
		[frame.window orderOut:nil];
}

void closeEditor ()
{
	if (frame.activationObserver)
	{
		[NSWorkspace.sharedWorkspace.notificationCenter removeObserver:frame.activationObserver];
		frame.activationObserver = nil;
	}
	frame.detachView ();
	if (frame.window)
	{
		frame.window.delegate = nil;
		[frame.window orderOut:nil];
		[frame.window close];
	}
	frame.window = nil;
	frame.delegate = nil;
}

} // namespace Lucent
