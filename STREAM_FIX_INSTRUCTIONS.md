# CRITICAL FIX: Stream Handling for Canon EDSDK

## Problem
Your log shows downloads are "successful" but files don't appear on disk. This happens because:

1. EdsDownload reports success
2. EdsDownloadComplete reports success 
3. We delete from camera
4. BUT the stream isn't closed until the finally block
5. File isn't flushed to disk until stream is closed

## Solution
Replace the ProcessImageDownload method's success handling section with this:

```csharp
                uint complete = CanonEdSdkNative.EdsDownloadComplete(dirItemRef);
                LogMessage($"Canon: EdsDownloadComplete = 0x{complete:X8} ({CanonEdSdkNative.GetErrorDescription(complete)})");

                // CRITICAL: Close stream handle IMMEDIATELY to flush data to disk
                if (streamRef != IntPtr.Zero)
                {
                    SafeEdsRelease(streamRef, "Stream handle");
                    streamRef = IntPtr.Zero; // Prevent double-release in finally
                    LogMessage($"Canon: Stream closed - file should now be flushed to disk");
                }

                if (downloadStarted && complete == CanonEdSdkNative.EDS_ERR_OK)
                {
                    // Give file system time to complete write operations
                    await Task.Delay(200).ConfigureAwait(false);
                    
                    // Verify file exists and has content BEFORE deleting from camera
                    if (File.Exists(targetPath))
                    {
                        var fi = new FileInfo(targetPath);
                        if (fi.Length > 0)
                        {
                            LogMessage($"Canon: SUCCESS - File verified on disk: {Path.GetFileName(targetPath)} ({fi.Length:N0} bytes)");
                            
                            // ONLY delete from camera AFTER confirming file exists on disk
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
                        LogMessage($"Canon: ERROR - File not found after download: {targetPath}");
                        
                        // Check what files are actually in the directory
                        try
                        {
                            var dir = Path.GetDirectoryName(targetPath);
                            var files = Directory.GetFiles(dir, "*.*");
                            LogMessage($"Canon: Files in directory: {string.Join(", ", files.Select(Path.GetFileName))}");
                        }
                        catch
                        {
                            LogMessage($"Canon: Could not list directory contents");
                        }
                    }
                }
                else
                {
                    LogMessage($"Canon: Download failed - started={downloadStarted}, complete=0x{complete:X8}");
                }

                // Also update the finally block to:
                finally
                {
                    // streamRef is now set to IntPtr.Zero above, so no double-release
                    if (streamRef != IntPtr.Zero) SafeEdsRelease(streamRef, "Stream handle");
                    if (dirItemRef != IntPtr.Zero) SafeEdsRelease(dirItemRef, "Directory item handle");
                }
```

## Key Changes

1. **Close stream immediately** after EdsDownloadComplete
2. **Wait 200ms** for file system to finalize
3. **Check file exists** before deleting from camera
4. **Only delete from camera** if file is confirmed on disk
5. **Add directory listing** if file not found for debugging

This will ensure the file is actually written to disk before we report success!
