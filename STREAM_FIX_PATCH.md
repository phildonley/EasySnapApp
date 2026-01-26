// Critical Stream Handling Fix for CanonCameraService.cs
// 
// ISSUE: Files not appearing on disk even though download reports success
// ROOT CAUSE: Stream handle not being closed immediately after EdsDownloadComplete
// 
// Replace the ProcessImageDownload method's stream handling with this fixed version:

// After this line:
// uint complete = CanonEdSdkNative.EdsDownloadComplete(dirItemRef);

// REPLACE THE SUCCESS HANDLING SECTION WITH:

                uint complete = CanonEdSdkNative.EdsDownloadComplete(dirItemRef);
                LogMessage($"Canon: EdsDownloadComplete = 0x{complete:X8} ({CanonEdSdkNative.GetErrorDescription(complete)})");

                // CRITICAL FIX: Close stream IMMEDIATELY after download complete to flush file to disk
                if (streamRef != IntPtr.Zero)
                {
                    SafeEdsRelease(streamRef, "Stream handle");
                    streamRef = IntPtr.Zero; // Prevent double-release in finally
                    LogMessage($"Canon: Stream closed - file should now be flushed to disk");
                }

                if (downloadStarted && complete == CanonEdSdkNative.EDS_ERR_OK)
                {
                    // Small delay to allow file system to finalize
                    await Task.Delay(100).ConfigureAwait(false);
                    
                    // Verify the file actually exists and has content
                    if (File.Exists(targetPath))
                    {
                        var fi = new FileInfo(targetPath);
                        if (fi.Length > 0)
                        {
                            LogMessage($"Canon: SUCCESS - File verified on disk: {Path.GetFileName(targetPath)} ({fi.Length:N0} bytes)");
                            
                            // Delete from camera only AFTER confirming file is on disk
                            uint del = CanonEdSdkNative.EdsDeleteDirectoryItem(dirItemRef);
                            LogMessage($"Canon: EdsDeleteDirectoryItem = 0x{del:X8} ({CanonEdSdkNative.GetErrorDescription(del)})");

                            Application.Current?.Dispatcher?.Invoke(() => PhotoSaved?.Invoke(targetPath));
                        }
                        else
                        {
                            LogMessage($"Canon: ERROR - File exists but is empty: {targetPath}");
                        }
                    }
                    else
                    {
                        LogMessage($"Canon: ERROR - File not found on disk after download: {targetPath}");
                        LogMessage($"Canon: Check if stream was properly closed and path is accessible");
                    }
                }

// ALSO UPDATE THE FINALLY BLOCK TO:
            finally
            {
                // streamRef is now set to IntPtr.Zero above, so this won't double-release
                if (streamRef != IntPtr.Zero) SafeEdsRelease(streamRef, "Stream handle");
                if (dirItemRef != IntPtr.Zero) SafeEdsRelease(dirItemRef, "Directory item handle");
            }
