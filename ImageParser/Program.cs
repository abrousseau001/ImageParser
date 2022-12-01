// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using Serilog;
using Serilog.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("processingLog.txt") // log file.
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
    .CreateLogger();

imgDictionary.TryAdd(UnknownFormat, new List<string>());
imgDictionary.TryAdd(UnknownContent, new List<string>());
imgDictionary.TryAdd(Unreadable, new List<string>());

var imgDirectory = new DirectoryInfo(@"my path");

InspectImages(imgDirectory);
CopyImages(new DirectoryInfo(@"path to organized files"));


internal partial class Program
{
    private static ConcurrentDictionary<string, List<string>> imgDictionary = new();

    private const string UnknownContent = "UnknownContent";
    private const string UnknownFormat = "UnknownFormat";
    private const string Unreadable = "Unreadable";

    private static void CopyImages(DirectoryInfo copyImgDirectory)
    {
        foreach (var currentKeyValue in imgDictionary)
        {
            if (currentKeyValue.Key == Unreadable || currentKeyValue.Key == UnknownContent ||
                currentKeyValue.Key == UnknownFormat) continue;
            
            var baseCopyPath = Path.Join(copyImgDirectory.FullName, currentKeyValue.Key);
            
            if(!Directory.Exists(baseCopyPath)){
                copyImgDirectory.CreateSubdirectory(currentKeyValue.Key);
            }
            
            Log.Information("Copying files to {BaseCopyPath}", baseCopyPath);
            
            Parallel.ForEach(currentKeyValue.Value, currentValue =>
            {
                var fileInfo = new FileInfo(currentValue);
                fileInfo.CopyTo($"{baseCopyPath}/{fileInfo.Name}");
            });
        }
    }

    private static void InspectImages(DirectoryInfo currentImgDirectory)
    {
        Parallel.ForEach(currentImgDirectory.GetFileSystemInfos(), currentFileInfo =>
        {
            if ((currentFileInfo.Attributes & FileAttributes.Directory) != 0)
            {
                InspectImages((DirectoryInfo)currentFileInfo);
            }
            else
            {
                try
                {
                    var img = Image.Load(currentFileInfo.FullName);
                    DateTime dt;

                    if (img.Metadata.ExifProfile != null)
                    {
                        var dateTimeMetadata =
                            img.Metadata.ExifProfile.Values.FirstOrDefault(currentPart =>
                                currentPart.Tag.Equals(ExifTag.DateTime));

                        if (dateTimeMetadata?.GetValue() != null)
                        {
                            Log.Information($"{currentFileInfo.FullName} : {dateTimeMetadata.GetValue()}");
                            _ = DateTime.TryParse(dateTimeMetadata.GetValue().ToString(), out dt);

                            if (dt == DateTime.MinValue)
                            {
                                // Failed to parse so the format is probably strange or the date is not valid
                                // e.g. "2016:03:10 12:19:14"
                                //
                                // This format isn't close to anything any of the calendars support, so replace the space between the
                                // date with a colon (:) then split on the colon to get the year and month to build the DateTime
                                //
                                // Some of the dates are invalid, like 4-31-2013 (no 31st day in April) so ignore the day portion
                                // since it isn't being used anyways
                                var dateArray = dateTimeMetadata?.GetValue().ToString().Replace(" ", ":").Split(":");
                                dt = new DateTime(int.Parse(dateArray[0]), int.Parse( dateArray[1]), 1);
                            }
                        }
                        else
                        {
                            dt = currentFileInfo.CreationTime < currentFileInfo.LastWriteTime
                                ? currentFileInfo.CreationTime
                                : currentFileInfo.LastWriteTime;
                            Log.Information("{ObjFullName} : {Dt}", currentFileInfo.FullName, dt);
                        }

                        var keyFormat = $"{dt.Year}_{dt.Month}";

                        if (imgDictionary.Keys.Contains(keyFormat))
                        {
                            imgDictionary.GetValueOrDefault(keyFormat)?.Add(currentFileInfo.FullName);
                        }
                        else
                        {
                            imgDictionary.TryAdd(keyFormat, new List<string> { currentFileInfo.FullName });
                        }
                    }
                    else
                    {
                        dt = currentFileInfo.CreationTime < currentFileInfo.LastWriteTime
                            ? currentFileInfo.CreationTime
                            : currentFileInfo.LastWriteTime;
                        
                        Log.Information("{ObjFullName} : {Dt}", currentFileInfo.FullName, dt);

                        var keyFormat = $"{dt.Year}_{dt.Month}";

                        if (imgDictionary.Keys.Contains(keyFormat))
                        {
                            imgDictionary.GetValueOrDefault(keyFormat)?.Add(currentFileInfo.FullName);
                        }
                        else
                        {
                            imgDictionary.TryAdd(keyFormat, new List<string> { currentFileInfo.FullName });
                        }
                    }
                }
                catch (UnknownImageFormatException unknownImageFormatException)
                {
                    Log.Error($"Unknown format for: ${currentFileInfo.FullName}");

                    imgDictionary.GetValueOrDefault(UnknownFormat)?.Add(currentFileInfo.FullName);

                }
                catch (InvalidImageContentException invalidImageContentException)
                {
                    Log.Error(
                        $"Image is not in standard format and is unparsable: ${currentFileInfo.FullName}");

                    imgDictionary.GetValueOrDefault(UnknownContent)?.Add(currentFileInfo.FullName);
                }
                catch (IOException ioException)
                {
                    Log.Error($"Image is not readable (possibly corrupted): ${currentFileInfo.FullName}");

                    imgDictionary.GetValueOrDefault(Unreadable)?.Add(currentFileInfo.FullName);
                }
                catch (NullReferenceException nullReferenceException)
                {
                    Log.Error($"Image is not readable (possibly corrupted): ${currentFileInfo.FullName}");

                    imgDictionary.GetValueOrDefault(Unreadable)?.Add(currentFileInfo.FullName);
                }
            }
        });
    }
}