#region Using directives
using System;
using UAManagedCore;
using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.UI;
using FTOptix.NetLogic;
using System.IO;
using System.Threading;
#endregion

public class ImageRefresher : BaseNetLogic
{
    public override void Start()
    {
        onImageChangedEvent = new AutoResetEvent(false);
        longRunningTask = new LongRunningTask(OnFileChangedAction, LogicObject);
        longRunningTask.Start();

        WatchImage();
    }

    public override void Stop()
    {
        UnWatchImage();
        closing = true;
        onImageChangedEvent.Set();
    }

    private void WatchImage()
    {
        imageObject = (Image)Owner;
        if (imageObject == null)
        {
            Log.Error("ImageRefresher", "Image not found");
            return;
        }

        // Determine image absolute path from QStudio path conventions
        imageAbsolutePath = imageObject.Path.Uri;
        if (string.IsNullOrEmpty(imageAbsolutePath))
        {
            Log.Error("ImageRefresher", "Image path variable cannot be empty");
            return;
        }

        // Start FileSystemWatcher for monitoring image changes
        StartFileSystemWatcher();
    }

    private void UnWatchImage()
    {
        fileSystemWatcher.Changed -= OnChanged;
        fileSystemWatcher.Created -= OnChanged;
        fileSystemWatcher.Dispose();
        CleanUpTemporaryFiles();
    }

    private void StartFileSystemWatcher()
    {
        fileSystemWatcher = new FileSystemWatcher();
        fileSystemWatcher.Path = Path.GetDirectoryName(imageAbsolutePath);
        fileSystemWatcher.Filter = Path.GetFileName(imageAbsolutePath);

        // Add event handler for monitoring file changes
        fileSystemWatcher.Changed += OnChanged;
        // Some applications (such as MS Paint) do not trigger the Changed event when an image is modified.
        // When saving, MS Paint first deletes the old image and then it creates a new one with the changes applied, so that Deleted and Created events are fired.
        // In that case it is necessary to monitor the Created event and act as if the Changed event had been fired.
        fileSystemWatcher.Created += OnChanged;

        // Begin watching selected files
        fileSystemWatcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object source, FileSystemEventArgs e)
    {
        lock (isWaitingLock)
        {
            // Multiple change events may be fired by the FileSystemWatcher when the file changes,
            // so within a span of 500 ms only one event is considered and the following ones are ignored
            if (isWaiting)
                return;
            isWaiting = true;
        }

        onImageChangedEvent.Set();
    }

    private void OnFileChangedAction(LongRunningTask task)
    {
        while (true)
        {
            if (task.IsCancellationRequested)
                return;

            onImageChangedEvent.WaitOne();
            if (closing)
                return;

            delayedTask = new DelayedTask(OnFileChangedDelayedAction, periodMilliseconds, LogicObject);
            delayedTask.Start();
        }
    }

    private void OnFileChangedDelayedAction()
    {
        CleanUpTemporaryFiles();
        SetModifiedImage();

        lock (isWaitingLock)
            isWaiting = false;
    }

    private void SetModifiedImage()
    {
        // Make a copy of the changed file, adding "~<counter>" as suffix (i.e. image~1.png).
        var temporaryImageName = Path.GetFileNameWithoutExtension(imageAbsolutePath) + String.Format("~{0}", counter) + Path.GetExtension(imageAbsolutePath);

        // Copy temporary image into ApplicationFiles
        var imageApplicationDirectoryPath = Path.Combine(Project.Current.ApplicationDirectory, temporaryImageName);

        try
        {
            File.Copy(imageAbsolutePath, imageApplicationDirectoryPath, true);
        }
        catch (Exception e)
        {
            Log.Error("ImageRefresher", $"Unable to copy image, exception {e.Message}");
            return;
        }

        try
        {
            // Set temporary image in imageObject Path (using QStudio path conventions)
            imageObject.Path = ResourceUri.FromApplicationRelativePath(temporaryImageName);
        }
        catch (Exception e)
        {
            Log.Error("ImageRefresher", $"Unable to assign Path variable: {e}");
            return;
        }

        ++counter;
    }

    private void CleanUpTemporaryFiles()
    {
        var filesToDelete = Path.GetFileNameWithoutExtension(imageAbsolutePath) + "~*";
        string[] fileList = Directory.GetFiles(Project.Current.ApplicationDirectory, filesToDelete);

        foreach (string file in fileList)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e)
            {
                Log.Error("ImageRefresher", $"Unable to delete image, exception {e.Message}");
            }
        }
    }

    private FileSystemWatcher fileSystemWatcher;
    private readonly object isWaitingLock = new object();
    private bool isWaiting = false;
    private Image imageObject;
    private string imageAbsolutePath = "";
    private uint counter = 1;
    private readonly int periodMilliseconds = 500;
    private DelayedTask delayedTask;
    private LongRunningTask longRunningTask;
    private AutoResetEvent onImageChangedEvent;
    private bool closing = false;
}
