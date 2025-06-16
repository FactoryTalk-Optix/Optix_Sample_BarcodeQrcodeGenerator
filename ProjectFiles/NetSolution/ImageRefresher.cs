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

    /// <summary>
    /// This method watches for changes in an image file path.
    /// If the image reference is not found or the image path is empty, it logs an error and exits.
    /// Otherwise, it starts a FileSystemWatcher to monitor changes to the image file.
    /// </summary>
    /// <remarks>
    /// - Image reference must exist.
    /// - Image path must contain a valid absolute URI.
    /// </remarks>
    private void WatchImage()
    {
        imageObject = (Image)Owner;
        if (imageObject == null)
        {
            Log.Error("ImageRefresher", "Image not found");
            return;
        }

        // Determine image absolute path from FTOptixStudio path conventions
        imageAbsolutePath = imageObject.Path.Uri;
        if (string.IsNullOrEmpty(imageAbsolutePath))
        {
            Log.Error("ImageRefresher", "Image path variable cannot be empty");
            return;
        }

        // Start FileSystemWatcher for monitoring image changes
        StartFileSystemWatcher();
    }

    /// <summary>
    /// Removes event handlers for changed and created events on the file system watcher,
    /// disposes the watcher, and cleans up temporary files.
    /// </summary>
    /// <remarks>
    /// This method is called when an image file is no longer watched by the application.
    /// It unregisters event handlers from the file system watcher, disposes the watcher object,
    /// and removes any temporary files associated with this image.
    /// </remarks>
    private void UnWatchImage()
    {
        fileSystemWatcher.Changed -= OnChanged;
        fileSystemWatcher.Created -= OnChanged;
        fileSystemWatcher.Dispose();
        CleanUpTemporaryFiles();
    }

    /// <summary>
    /// Initializes a FileSystemWatcher object for monitoring changes on the specified image file path.
    /// </summary>
    /// <remarks>
    /// The watcher will monitor the directory containing the image file and notify when the file is changed or created.
    /// </remarks>
    /// <param name="imageAbsolutePath">The absolute path to the image file.</param>
    /// <param name="fileSystemWatcher">The FileSystemWatcher instance initialized with the provided parameters.</param>
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

    /// <summary>
    /// This method locks an object named 'isWaitingLock' before executing critical operations related to file system changes.
    /// If the thread is already waiting for something else, it immediately returns without doing anything.
    /// It then sets the 'onImageChangedEvent' event after marking itself as waiting again.
    /// </summary>
    /// <param name="source">The source of the change event.</param>
    /// <param name="e">The event arguments containing details about the change.</param>
    /// <remarks>
    /// This method ensures that only one thread can execute the critical section at a time by locking the specified object.
    /// The 'onImageChangedEvent' is set once the critical operations are completed, indicating readiness for further processing or updates.
    /// </remarks>
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

    /// <summary>
    /// Continuously checks for file changes and executes tasks accordingly.
    /// <example>
    /// For example:
    /// <code>
    /// OnFileChangedAction(new LongRunningTask());
    /// </code>
    /// will start checking for file changes and executing tasks when necessary.
    /// </example>
    /// </summary>
    /// <param name="task">The task to be executed during file change events.</param>
    /// <remarks>
    /// The method runs indefinitely until it receives an indication that the cancellation request has been made or the process is shutting down.
    /// </remarks>
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

    /// <summary>
    /// This method triggers actions after file changes are detected.
    /// <example>
    /// For example:
    /// <code>
    /// OnFileChangedDelayedAction();
    /// </code>
    /// will execute <c>CleanUpTemporaryFiles()</c>, <c>SetModifiedImage()</c>, and set <c>isWaiting</c> to false.
    /// </example>
    /// </summary>
    /// <remarks>
    /// It uses a lock statement to ensure thread safety when modifying shared state.
    /// </remarks>
    private void OnFileChangedDelayedAction()
    {
        CleanUpTemporaryFiles();
        SetModifiedImage();

        lock (isWaitingLock)
            isWaiting = false;
    }

    /// <summary>
    /// This method sets the modified image by copying it from its original path to a temporary file with a unique suffix.
    /// It then updates the image object's path to reference this new temporary file.
    /// If an error occurs during either operation, an error message is logged.
    /// </summary>
    /// <remarks>
    /// Example usage:
    /// <code>
    /// ImageRefresher.SetModifiedImage();
    /// </code>
    /// </remarks>
    private void SetModifiedImage()
    {
        // Make a copy of the changed file, adding "~<counter>" as suffix (i.e. image~1.png).
        var temporaryImageName = Path.GetFileNameWithoutExtension(imageAbsolutePath) + String.Format("~{0}", counter) + Path.GetExtension(imageAbsolutePath);

        // Copy temporary image into ProjectFiles
        var imageProjectDirectoryPath = Path.Combine(Project.Current.ProjectDirectory, temporaryImageName);

        try
        {
            File.Copy(imageAbsolutePath, imageProjectDirectoryPath, true);
        }
        catch (Exception e)
        {
            Log.Error("ImageRefresher", $"Unable to copy image, exception {e.Message}");
            return;
        }

        try
        {
            // Set temporary image in imageObject Path (using FTOptixStudio path conventions)
            imageObject.Path = ResourceUri.FromProjectRelativePath(temporaryImageName);
        }
        catch (Exception e)
        {
            Log.Error("ImageRefresher", $"Unable to assign Path variable: {e}");
            return;
        }

        ++counter;
    }

    /// <summary>
    /// This method cleans up temporary files associated with the image.
    /// It searches for files with a specific naming pattern in the project directory and deletes them.
    /// If an error occurs during deletion, it logs the error message.
    /// </summary>
    private void CleanUpTemporaryFiles()
    {
        var filesToDelete = Path.GetFileNameWithoutExtension(imageAbsolutePath) + "~*";
        string[] fileList = Directory.GetFiles(Project.Current.ProjectDirectory, filesToDelete);

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
