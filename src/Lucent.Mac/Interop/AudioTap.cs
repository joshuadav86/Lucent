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
    private static partial IntPtr IntPtr_objc_msgSend_Byte(IntPtr receiver, IntPtr selector, byte argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);
}
