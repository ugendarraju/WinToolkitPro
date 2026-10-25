using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;

namespace WinToolkit.Services;

public partial class ActivityService : ObservableObject
{
    private static ActivityService? _instance;
    public static ActivityService Instance => _instance ??= new ActivityService();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _currentOperation = "Active Tasks & Status";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<ActivityTaskItem> ActiveTasks { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public ActivityService()
    {
        _instance = this;
    }

    [RelayCommand]
    public void OpenDrawer() => IsOpen = true;

    [RelayCommand]
    public void CloseDrawer() => IsOpen = false;

    [RelayCommand]
    public void ToggleDrawer() => IsOpen = !IsOpen;

    [RelayCommand]
    public void ClearLogs()
    {
        Application.Current?.Dispatcher?.Invoke(() => Logs.Clear());
    }

    private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinToolkit");
    private static readonly string LogFilePath = Path.Combine(LogDirectory, "activity.log");
    private static readonly object LogFileLock = new();

    public void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logLine = $"[{timestamp}] {message}";

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var shortTime = DateTime.Now.ToString("HH:mm:ss");
            Logs.Add($"[{shortTime}] {message}");
            if (Logs.Count > 500)
            {
                Logs.RemoveAt(0);
            }
        });

        Task.Run(() =>
        {
            try
            {
                lock (LogFileLock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
                }
            }
            catch { }
        });
    }

    [RelayCommand]
    public void OpenLogFile()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            if (!File.Exists(LogFilePath))
            {
                File.WriteAllText(LogFilePath, $"--- WinToolkit Pro Activity Log Started {DateTime.Now} ---" + Environment.NewLine);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddLog($"Could not open log file: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearFinished()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var finished = ActiveTasks.Where(t => t.Status == ActivityTaskStatus.Completed || t.Status == ActivityTaskStatus.Failed).ToList();
            foreach (var task in finished)
            {
                ActiveTasks.Remove(task);
            }
            RecalculateMetrics();
        });
    }

    [RelayCommand]
    public void ClearAll()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            ActiveTasks.Clear();
            RecalculateMetrics();
        });
    }

    [RelayCommand]
    public void RemoveTask(ActivityTaskItem task)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (task != null)
            {
                ActiveTasks.Remove(task);
                RecalculateMetrics();
            }
        });
    }

    public void StartSession(string operationTitle, IEnumerable<ActivityTaskItem> tasks)
    {
        var taskList = tasks.ToList();
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            IsOpen = true;

            foreach (var task in taskList)
            {
                if (string.IsNullOrEmpty(task.Category))
                {
                    task.Category = operationTitle;
                }

                // Append or update existing task by Id so previous actions are never erased
                var existing = ActiveTasks.FirstOrDefault(t => t.Id.Equals(task.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = task.Name;
                    existing.Category = task.Category;
                    existing.Status = task.Status;
                    existing.StatusMessage = task.StatusMessage;
                    existing.Progress = task.Progress;
                }
                else
                {
                    ActiveTasks.Add(task);
                }
            }

            RecalculateMetrics();
        });

        AddLog($"Started {operationTitle} ({taskList.Count} item(s))");
    }

    public void UpdateTask(string taskId, ActivityTaskStatus status, string statusMessage, double progress = 0)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        void ApplyUpdate()
        {
            var existing = ActiveTasks.FirstOrDefault(t => t.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Status = status;
                existing.StatusMessage = statusMessage;
                existing.Progress = Math.Clamp(progress, 0, 100);
            }
            RecalculateMetrics();
        }

        if (dispatcher.CheckAccess())
        {
            ApplyUpdate();
        }
        else
        {
            dispatcher.BeginInvoke(ApplyUpdate);
        }
    }

    public void UpdateOverallProgress(double progress, string? status = null)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (ActiveTasks.Count <= 1)
            {
                OverallProgress = Math.Clamp(progress, 0, 100);
                if (!string.IsNullOrEmpty(status))
                {
                    StatusText = status;
                }
            }
            else
            {
                RecalculateMetrics();
            }
        });
    }

    public void CompleteSession(string completionMessage)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            AddLog(completionMessage);
            RecalculateMetrics();
        });
    }

    private void RecalculateMetrics()
    {
        if (ActiveTasks.Count == 0)
        {
            CurrentOperation = "Active Tasks & Status";
            StatusText = "No active tasks.";
            OverallProgress = 0;
            IsBusy = false;
            return;
        }

        var running = ActiveTasks.Count(t => t.Status == ActivityTaskStatus.Running);
        var queued = ActiveTasks.Count(t => t.Status == ActivityTaskStatus.Queued);
        var completed = ActiveTasks.Count(t => t.Status == ActivityTaskStatus.Completed);
        var failed = ActiveTasks.Count(t => t.Status == ActivityTaskStatus.Failed);
        var finished = completed + failed;
        var total = ActiveTasks.Count;

        IsBusy = (running > 0 || queued > 0);

        if (!IsBusy)
        {
            OverallProgress = 100;
            StatusText = failed > 0
                ? $"{completed} succeeded, {failed} failed"
                : $"All {total} tasks completed!";
            CurrentOperation = "Tasks Completed";
        }
        else
        {
            var sumProgress = ActiveTasks.Sum(t => t.Progress);
            OverallProgress = Math.Clamp(sumProgress / total, 0, 99.9);
            StatusText = $"{running} in progress • {finished} of {total} completed";

            var activeOps = ActiveTasks.Where(t => t.Status == ActivityTaskStatus.Running || t.Status == ActivityTaskStatus.Queued)
                                       .Select(t => t.Category)
                                       .Where(c => !string.IsNullOrEmpty(c))
                                       .Distinct()
                                       .ToList();
            if (activeOps.Count == 1)
            {
                CurrentOperation = activeOps[0];
            }
            else
            {
                CurrentOperation = $"Active Tasks ({running} Running)";
            }
        }
    }
}
