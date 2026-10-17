using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>
/// A secret kept in the user's login keychain as a generic password, through the Security framework.
/// macOS encrypts it with the user's keychain and only lets the app that saved it read it back
/// without asking (the Mac's equivalent of Windows' per-user DPAPI protection).
/// </summary>
internal static partial class Keychain
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8Encoding = 0x08000100;

    /// <summary>The item isn't in the keychain.</summary>
    public const int ItemNotFound = -25300;

    private static readonly Lazy<Symbols> Loaded = new(() => new Symbols());

    /// <summary>The saved secret, or null when there's none. Throws <see cref="KeychainException"/> if it can't be read.</summary>
    public static byte[]? Read(string service, string account)
    {
        var symbols = Loaded.Value;
        using var query = new CFDictionaryBuilder(symbols)
            .Add(symbols.Class, symbols.ClassGenericPassword)
            .Add(symbols.Service, service)
            .Add(symbols.Account, account)
            .Add(symbols.ReturnData, symbols.True)
            .Add(symbols.MatchLimit, symbols.MatchLimitOne);
        var status = SecItemCopyMatching(query.Build(), out var data);
        if (status == ItemNotFound)
        {
            return null;
        }

        if (status != 0)
        {
            throw new KeychainException(status);
        }

        try
        {
            var bytes = new byte[(int)CFDataGetLength(data)];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            CFRelease(data);
        }
    }

    /// <summary>Saves the secret, replacing one saved before. Throws <see cref="KeychainException"/> if it can't.</summary>
    public static void Write(string service, string account, string label, byte[] secret)
    {
        Delete(service, account);
        var symbols = Loaded.Value;
        using var item = new CFDictionaryBuilder(symbols)
            .Add(symbols.Class, symbols.ClassGenericPassword)
            .Add(symbols.Service, service)
            .Add(symbols.Account, account)
            .Add(symbols.Label, label)
            .Add(symbols.ValueData, secret);
        var status = SecItemAdd(item.Build(), IntPtr.Zero);
        if (status != 0)
        {
            throw new KeychainException(status);
        }
    }

    /// <summary>Removes the secret. Returns false if it was there but couldn't be removed.</summary>
    public static bool Delete(string service, string account)
    {
        var symbols = Loaded.Value;
        using var query = new CFDictionaryBuilder(symbols)
            .Add(symbols.Class, symbols.ClassGenericPassword)
            .Add(symbols.Service, service)
            .Add(symbols.Account, account);
        var status = SecItemDelete(query.Build());
        return status is 0 or ItemNotFound;
    }

    /// <summary>The Security framework's description of a status code, like "The user name or passphrase you entered is not correct."</summary>
    public static string Describe(int status)
    {
        try
        {
            var text = SecCopyErrorMessageString(status, IntPtr.Zero);
            if (text == IntPtr.Zero)
            {
                return $"error {status}";
            }

            try
            {
                var length = CFStringGetLength(text);
                var size = CFStringGetMaximumSizeForEncoding(length, Utf8Encoding) + 1;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    return CFStringGetCString(text, buffer, size, Utf8Encoding) ? Marshal.PtrToStringUTF8(buffer) ?? $"error {status}" : $"error {status}";
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CFRelease(text);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return $"error {status}";
        }
    }

    /// <summary>The framework constants the queries are made of (kSecClass and friends), read once.</summary>
    private sealed class Symbols
    {
        public Symbols()
        {
            var security = NativeLibrary.Load(Security);
            var coreFoundation = NativeLibrary.Load(CoreFoundation);
            IntPtr Constant(IntPtr library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

            Class = Constant(security, "kSecClass");
            ClassGenericPassword = Constant(security, "kSecClassGenericPassword");
            Service = Constant(security, "kSecAttrService");
            Account = Constant(security, "kSecAttrAccount");
            Label = Constant(security, "kSecAttrLabel");
            ValueData = Constant(security, "kSecValueData");
            ReturnData = Constant(security, "kSecReturnData");
            MatchLimit = Constant(security, "kSecMatchLimit");
            MatchLimitOne = Constant(security, "kSecMatchLimitOne");
            True = Constant(coreFoundation, "kCFBooleanTrue");
            KeyCallbacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
            ValueCallbacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks");
        }

        public IntPtr Class { get; }
        public IntPtr ClassGenericPassword { get; }
        public IntPtr Service { get; }
        public IntPtr Account { get; }
        public IntPtr Label { get; }
        public IntPtr ValueData { get; }
        public IntPtr ReturnData { get; }
        public IntPtr MatchLimit { get; }
        public IntPtr MatchLimitOne { get; }
        public IntPtr True { get; }
        public IntPtr KeyCallbacks { get; }
        public IntPtr ValueCallbacks { get; }
    }

    /// <summary>Builds a CFDictionary, and releases it and the CF objects made for it afterwards.</summary>
    private sealed class CFDictionaryBuilder : IDisposable
    {
        private readonly Symbols _symbols;
        private readonly List<IntPtr> _keys = new();
        private readonly List<IntPtr> _values = new();
        private readonly List<IntPtr> _owned = new();
        private IntPtr _dictionary;

        public CFDictionaryBuilder(Symbols symbols) => _symbols = symbols;

        public CFDictionaryBuilder Add(IntPtr key, IntPtr value)
        {
            _keys.Add(key);
            _values.Add(value);
            return this;
        }

        public CFDictionaryBuilder Add(IntPtr key, string value)
        {
            var text = CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding);
            _owned.Add(text);
            return Add(key, text);
        }

        public unsafe CFDictionaryBuilder Add(IntPtr key, byte[] value)
        {
            IntPtr data;
            fixed (byte* bytes = value)
            {
                data = CFDataCreate(IntPtr.Zero, (IntPtr)bytes, value.Length);
            }

            _owned.Add(data);
            return Add(key, data);
        }

        public unsafe IntPtr Build()
        {
            var keys = _keys.ToArray();
            var values = _values.ToArray();
            fixed (IntPtr* keyPointer = keys)
            fixed (IntPtr* valuePointer = values)
            {
                _dictionary = CFDictionaryCreate(IntPtr.Zero, (IntPtr)keyPointer, (IntPtr)valuePointer, keys.Length,
                    _symbols.KeyCallbacks, _symbols.ValueCallbacks);
            }

            _owned.Add(_dictionary);
            return _dictionary;
        }

        public void Dispose()
        {
            foreach (var item in _owned.Where(item => item != IntPtr.Zero))
            {
                CFRelease(item);
            }

            _owned.Clear();
        }
    }

    [LibraryImport(Security)]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemDelete(IntPtr query);

    [LibraryImport(Security)]
    private static partial IntPtr SecCopyErrorMessageString(int status, IntPtr reserved);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(IntPtr text);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr text, IntPtr buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataCreate(IntPtr allocator, IntPtr bytes, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr keys, IntPtr values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr item);
}

/// <summary>The keychain refused, with the Security framework's status code and its description.</summary>
internal sealed class KeychainException : Exception
{
    public KeychainException(int status)
        : base(Keychain.Describe(status)) => Status = status;

    public int Status { get; }
}
