using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>
/// Core Audio's process taps (macOS 14.2 and later): a tap hears what every app is playing, and a private
/// aggregate device built around it hands that sound to an IO callback, as if it were a microphone. See
/// Audio/SystemAudio.cs. The first tap asks the user, once, whether Lucent may record the Mac's sound,
/// with the reason in Info.plist's NSAudioCaptureUsageDescription.
/// </summary>
internal static unsafe partial class AudioTap
{
    private const string CoreAudioFramework = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    public const uint TapFormatProperty = 0x74666D74; // 'tfmt': the tap's AudioStreamBasicDescription
    public const uint NonInterleavedFlag = 1 << 5;    // kAudioFormatFlagIsNonInterleaved
    public const uint TapDescriptionProperty = 0x74647363; // 'tdsc': kAudioTapPropertyDescription

    static AudioTap()
    {
        // CATapDescription is an Objective-C class in Core Audio, so the framework has to be loaded before it's looked up.
        NativeLibrary.TryLoad(CoreAudioFramework, out _);
    }

    /// <summary>Process taps came with macOS 14.2.</summary>
    public static bool IsSupported => OperatingSystem.IsMacOSVersionAtLeast(14, 2);

    /// <summary>
    /// [[CATapDescription alloc] initStereoGlobalTapButExcludeProcesses:@[]]: a stereo mix of every app's sound,
    /// which the apps still play as normal. Returns the description (to release) and its UUID's text.
    /// </summary>
    public static (IntPtr Description, string Uuid) DescribeGlobalTap(string name)
    {
        var description = ObjC.Send(ObjC.Send(ObjC.Class("CATapDescription"), "alloc"), "initStereoGlobalTapButExcludeProcesses:",
            ObjC.Send(ObjC.Class("NSArray"), "array"));
        return Finish(description, name);
    }

    // Names the tap, hides it from other apps, and reads its UUID.
    private static (IntPtr Description, string Uuid) Finish(IntPtr description, string name)
    {
        if (description == IntPtr.Zero)
        {
            throw new IOException("macOS wouldn't describe a tap of the Mac's sound.");
        }

        ObjC.SendVoid(description, "setName:", NsString(name));
        // Hidden from other apps' lists of devices. (The property is privateTap, read as isPrivate.)
        foreach (var setter in new[] { "setPrivateTap:", "setPrivate:" })
        {
            if (ObjC.SendBool(description, "respondsToSelector:", ObjC.Selector(setter)))
            {
                ObjC.SendVoid(description, setter, true);
                break;
            }
        }

        var uuid = CoreAudio.ReadString(ObjC.Send(ObjC.Send(description, "UUID"), "UUIDString"))
            ?? throw new IOException("macOS gave the tap of the Mac's sound no ID.");
        return (description, uuid);
    }

    /// <summary>
    /// [[CATapDescription alloc] initStereoMixdownOfProcesses:]: a stereo mix of just these processes' sound (Core
    /// Audio's process objects, from <see cref="ProcessObjects"/>). Returns the description (to release) and its UUID's text.
    /// </summary>
    public static (IntPtr Description, string Uuid) DescribeAppTap(string name, IReadOnlyList<uint> processes)
    {
        var list = ObjC.Send(ObjC.Class("NSMutableArray"), "array");
        foreach (var process in processes)
        {
            ObjC.SendVoid(list, "addObject:", IntPtr_objc_msgSend_UInt32(ObjC.Class("NSNumber"), ObjC.Selector("numberWithUnsignedInt:"), process));
        }

        var description = ObjC.Send(ObjC.Send(ObjC.Class("CATapDescription"), "alloc"), "initStereoMixdownOfProcesses:", list);
        return Finish(description, name);
    }

    /// <summary>
    /// Has macOS quieten the tapped apps' own sound for as long as the tap is being read (CATapMutedWhenTapped), so
    /// Lucent can play it instead. If Lucent stops reading (it quits, or crashes), the apps are heard again.
    /// </summary>
    public static void MuteWhileTapped(uint tap, IntPtr description)
    {
        ObjC.SendVoid(description, "setMuteBehavior:", (ulong)2);
        var address = new CoreAudio.AudioObjectPropertyAddress(TapDescriptionProperty);
        var value = description;
        var result = CoreAudio.AudioObjectSetPropertyData(tap, &address, 0, null, (uint)sizeof(IntPtr), &value);
        if (result != 0)
        {
            throw new IOException($"macOS wouldn't quieten the app to play it through the equalizer (error {result}).");
        }
    }

    /// <summary>
    /// Core Audio's objects for the running processes whose bundle ID starts with one of <paramref name="bundlePrefixes"/>
    /// (com.google.Chrome takes in its helpers, com.google.Chrome.helper), in order. Only processes that use sound have one.
    /// </summary>
    public static List<uint> ProcessObjects(IReadOnlyList<string> bundlePrefixes)
    {
        var found = new List<uint>();
        var address = new CoreAudio.AudioObjectPropertyAddress(ProcessListProperty);
        if (CoreAudio.AudioObjectGetPropertyDataSize(CoreAudio.SystemObject, &address, 0, null, out var size) != 0 || size == 0)
        {
            return found;
        }

        var processes = new uint[size / sizeof(uint)];
        fixed (uint* list = processes)
        {
            if (CoreAudio.AudioObjectGetPropertyData(CoreAudio.SystemObject, &address, 0, null, ref size, list) != 0)
            {
                return found;
            }
        }

        foreach (var process in processes.Take((int)(size / sizeof(uint))))
        {
            var bundle = IntPtr.Zero;
            var bundleAddress = new CoreAudio.AudioObjectPropertyAddress(BundleIdProperty);
            var bundleSize = (uint)sizeof(IntPtr);
            if (CoreAudio.AudioObjectGetPropertyData(process, &bundleAddress, 0, null, ref bundleSize, &bundle) != 0 || bundle == IntPtr.Zero)
            {
                continue;
            }

            var id = CoreAudio.ReadString(bundle);
            CoreAudio.CFRelease(bundle);
            if (id is not null && bundlePrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(process);
            }
        }

        found.Sort();
        return found;
    }

    private const uint ProcessListProperty = 0x70727323; // 'prs#': kAudioHardwarePropertyProcessObjectList
    private const uint BundleIdProperty = 0x70626964;    // 'pbid': kAudioProcessPropertyBundleID

    /// <summary>
    /// The description of a private aggregate device that's clocked by <paramref name="outputUid"/> (the output
    /// being listened to) and hears the tap <paramref name="tapUuid"/>, as an NSDictionary (a CFDictionary).
    /// The keys are the kAudioAggregateDevice… and kAudioSub… constants' values.
    /// </summary>
    public static IntPtr DescribeAggregate(string uid, string name, string outputUid, string tapUuid)
    {
        var subDevice = Dictionary(("uid", NsString(outputUid)));
        var tap = Dictionary(("uid", NsString(tapUuid)), ("drift", NsBool(true)));
        return Dictionary(
            ("uid", NsString(uid)),
            ("name", NsString(name)),
            ("master", NsString(outputUid)),
            ("private", NsBool(true)),
            ("stacked", NsBool(false)),
            ("tapautostart", NsBool(true)),
            ("subdevices", Array(subDevice)),
            ("taps", Array(tap)));
    }

    // An autoreleased NSString (the caller's loop has an autorelease pool, or the objects live until the app ends).
    private static IntPtr NsString(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* utf8 = bytes)
        {
            return ObjC.Send(ObjC.Class("NSString"), "stringWithUTF8String:", (IntPtr)utf8);
        }
    }

    private static IntPtr NsBool(bool value) => IntPtr_objc_msgSend_Byte(ObjC.Class("NSNumber"), ObjC.Selector("numberWithBool:"), value ? (byte)1 : (byte)0);

    private static IntPtr Array(IntPtr item) => ObjC.Send(ObjC.Class("NSArray"), "arrayWithObject:", item);

    private static IntPtr Dictionary(params (string Key, IntPtr Value)[] entries)
    {
        var dictionary = ObjC.Send(ObjC.Class("NSMutableDictionary"), "dictionary");
        foreach (var (key, value) in entries)
        {
            void_objc_msgSend_IntPtr_IntPtr(dictionary, ObjC.Selector("setObject:forKey:"), value, NsString(key));
        }

        return dictionary;
    }

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioHardwareCreateProcessTap(IntPtr description, out uint tap);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioHardwareDestroyProcessTap(uint tap);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioHardwareCreateAggregateDevice(IntPtr description, out uint device);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioHardwareDestroyAggregateDevice(uint device);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioDeviceCreateIOProcID(uint device,
        delegate* unmanaged<uint, IntPtr, CoreAudio.AudioBufferList*, IntPtr, CoreAudio.AudioBufferList*, IntPtr, IntPtr, int> proc,
        IntPtr clientData, out IntPtr procId);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioDeviceDestroyIOProcID(uint device, IntPtr procId);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioDeviceStart(uint device, IntPtr procId);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioDeviceStop(uint device, IntPtr procId);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioObjectAddPropertyListener(uint objectId, CoreAudio.AudioObjectPropertyAddress* address,
        delegate* unmanaged<uint, uint, CoreAudio.AudioObjectPropertyAddress*, IntPtr, int> listener, IntPtr clientData);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioObjectRemovePropertyListener(uint objectId, CoreAudio.AudioObjectPropertyAddress* address,
        delegate* unmanaged<uint, uint, CoreAudio.AudioObjectPropertyAddress*, IntPtr, int> listener, IntPtr clientData);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_UInt32(IntPtr receiver, IntPtr selector, uint argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_Byte(IntPtr receiver, IntPtr selector, byte argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);
}
