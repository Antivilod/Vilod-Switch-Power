using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using QuietPC.Windows;

// Integration checks use disposable cloned schemes, never edit the original scheme.
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Disposable Windows runner required.");
if (args.Length != 1) throw new ArgumentException("Supply the JSON result path.");
var power = new WindowsPower();
var original = power.Active();
var originalState = power.Read(original);
var owned = new List<Guid>();
int eppClasses = 0, minimumClasses = 0;
bool policies = false, restored = false;
try {
    var source = Native.Duplicate(original); owned.Add(source);
    Native.Write(source, Native.Boost, 0, 0);
    Native.Write(source, Native.Minimum, 3, 65);
    foreach (var setting in Native.EppClasses)
        if (Native.Read(source, setting) is not null) { Native.Write(source, setting, 80, 60); eppClasses++; }
    foreach (var setting in Native.MinimumClasses)
        if (Native.Read(source, setting) is not null) { Native.Write(source, setting, 3, 65); minimumClasses++; }
    var sourceState = power.Read(source);
    foreach (uint epp in new uint[] {90, 70, 0}) {
        var clone = power.Clone(source, epp); owned.Add(clone.Id);
        Native.Expect(clone.Id, Native.Epp, epp, epp);
        Native.Expect(clone.Id, Native.Minimum, 3, 5);
        uint turbo = epp >= 80 ? 0u : epp == 0 ? 2u : 1u;
        Native.Expect(clone.Id, Native.Boost, turbo, turbo);
        foreach (var setting in Native.EppClasses)
            if (Native.Read(source, setting) is not null) Native.Expect(clone.Id, setting, epp, epp);
        foreach (var setting in Native.MinimumClasses)
            if (Native.Read(source, setting) is not null) Native.Expect(clone.Id, setting, 3, 5);
        power.Activate(clone.Id);
        if (power.Active() != clone.Id) throw new IOException("Clone activation was not confirmed.");
        power.Activate(original);
        if (power.Active() != original || power.Read(original) != originalState || power.Read(source) != sourceState)
            throw new IOException("Source or original power scheme changed.");
    }
    policies = true;
} finally {
    // Restoration and cleanup must finish before a success report is written.
    power.Activate(original);
    foreach (var id in owned.AsEnumerable().Reverse()) power.Delete(id);
    restored = power.Active() == original && power.Read(original) == originalState;
}
if (!policies || !restored) throw new IOException("CPU-policy verification failed.");
File.WriteAllText(args[0], JsonSerializer.Serialize(new {
    version = "0.8.3", platform = "Windows hosted runner",
    epp_ac_dc = true, minimum_ac_dc = true, turbo_0_1_2 = true,
    gaming_from_turbo_disabled_base = true, activation_restore = restored,
    additional_epp_classes_tested = eppClasses,
    additional_minimum_classes_tested = minimumClasses,
    physical_hybrid_hardware_tested = false
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: CPU policy AC/DC, Turbo, activation and restoration; additional EPP classes={eppClasses}, minimum classes={minimumClasses}. Physical hybrid hardware was not tested.");

static class Native {
    static Guid Cpu = new("54533251-82be-4824-96c1-47b60b740d00");
    internal static readonly Guid Epp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    internal static readonly Guid Minimum = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    internal static readonly Guid Boost = new("be337238-0d82-4146-a960-4f3749d470c7");
    internal static readonly Guid[] EppClasses = [new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864"), new("36687f9e-e3a5-4dbf-b1dc-15eb381c6865")];
    internal static readonly Guid[] MinimumClasses = [new("893dee8e-2bef-41e0-89c6-b55d0929964d"), new("893dee8e-2bef-41e0-89c6-b55d0929964e")];
    static void Check(uint rc) { if (rc != 0) throw new Win32Exception((int)rc); }
    internal static Guid Duplicate(Guid source) {
        Check(PowerDuplicateScheme(0, ref source, out var ptr));
        try { return Marshal.PtrToStructure<Guid>(ptr); } finally { LocalFree(ptr); }
    }
    internal static (uint Ac, uint Dc)? Read(Guid id, Guid setting) {
        uint ar = PowerReadACValueIndex(0, ref id, ref Cpu, ref setting, out var ac);
        uint dr = PowerReadDCValueIndex(0, ref id, ref Cpu, ref setting, out var dc);
        if (ar == 2 && dr == 2) return null;
        Check(ar); Check(dr); return (ac, dc);
    }
    internal static void Write(Guid id, Guid setting, uint ac, uint dc) {
        Check(PowerWriteACValueIndex(0, ref id, ref Cpu, ref setting, ac));
        Check(PowerWriteDCValueIndex(0, ref id, ref Cpu, ref setting, dc));
        Expect(id, setting, ac, dc);
    }
    internal static void Expect(Guid id, Guid setting, uint ac, uint dc) {
        if (Read(id, setting) != (ac, dc)) throw new IOException("CPU policy read-back mismatch.");
    }
    [DllImport("powrprof.dll")] static extern uint PowerDuplicateScheme(nint root, ref Guid source, out nint ptr);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(nint root, ref Guid id, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(nint root, ref Guid id, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(nint root, ref Guid id, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(nint root, ref Guid id, ref Guid group, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint ptr);
}
