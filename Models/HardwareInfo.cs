using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class HardwareInfo : ObservableObject
{
    // Processor
    public string CpuName { get; set; } = "Detecting...";
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public string CpuClockSpeed { get; set; } = string.Empty;
    public string CpuArchitecture { get; set; } = "64-bit";

    // Graphics
    public string GpuName { get; set; } = "Detecting...";
    public string GpuVram { get; set; } = string.Empty;
    public string GpuDriverVersion { get; set; } = string.Empty;

    // Memory
    public string RamTotal { get; set; } = "Detecting...";
    public string RamType { get; set; } = string.Empty;
    public string RamAvailable { get; set; } = string.Empty;
    public string RamSpeed { get; set; } = string.Empty;
    public string RamSlots { get; set; } = string.Empty;

    // Motherboard & BIOS
    public string MotherboardManufacturer { get; set; } = string.Empty;
    public string MotherboardModel { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public string BiosDate { get; set; } = string.Empty;

    // Storage
    public List<DiskDriveInfo> Disks { get; set; } = new();

    // Battery
    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private string _batteryStatus = "Unknown";

    [ObservableProperty]
    private int _batteryPercentage;

    [ObservableProperty]
    private string _batteryDesignCapacity = "--";

    [ObservableProperty]
    private string _batteryFullChargeCapacity = "--";

    [ObservableProperty]
    private double _batteryHealthPercent;

    [ObservableProperty]
    private string _batteryHealthGrade = "Detecting...";

    [ObservableProperty]
    private string _batteryCycleCount = "--";
}

public class DiskDriveInfo
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string TotalSize { get; set; } = string.Empty;
    public string FreeSize { get; set; } = string.Empty;
    public double UsedPercent { get; set; }
}
