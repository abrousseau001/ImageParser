// Copyright 2026 Adam Brousseau
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the “Software”), to deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
// persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS
// OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT
// OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Collections.Concurrent;
using System.Globalization;
using Serilog;
using Serilog.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("processingLog.txt") // log file.
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
    .CreateLogger();

_imgDictionary.TryAdd(UnknownFormat, []);
_imgDictionary.TryAdd(UnknownContent, []);
_imgDictionary.TryAdd(Unreadable, []);

var imgDirectory = new DirectoryInfo(@"my path");

InspectImages(imgDirectory);
CopyImages(new DirectoryInfo(@"path to organized files"));


internal partial class Program
{
    private static ConcurrentDictionary<string, List<string>> _imgDictionary = new();

    private const string UnknownContent = "UnknownContent";
    private const string UnknownFormat = "UnknownFormat";
    private const string Unreadable = "Unreadable";

    private static void CopyImages(DirectoryInfo copyImgDirectory)
    {
        foreach (var currentKeyValue in _imgDictionary)
        {
            if (currentKeyValue.Key is Unreadable or UnknownContent or UnknownFormat) continue;
            
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
                            Log.Information("{ObjFullName} : {GetValue}", currentFileInfo.FullName, dateTimeMetadata.GetValue());
                            _ = DateTime.TryParse(dateTimeMetadata.GetValue()?.ToString(), CultureInfo.InvariantCulture, out dt);

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

                        if (_imgDictionary.Keys.Contains(keyFormat))
                        {
                            _imgDictionary.GetValueOrDefault(keyFormat)?.Add(currentFileInfo.FullName);
                        }
                        else
                        {
                            _imgDictionary.TryAdd(keyFormat, [currentFileInfo.FullName]);
                        }
                    }
                    else
                    {
                        dt = currentFileInfo.CreationTime < currentFileInfo.LastWriteTime
                            ? currentFileInfo.CreationTime
                            : currentFileInfo.LastWriteTime;
                        
                        Log.Information("{ObjFullName} : {Dt}", currentFileInfo.FullName, dt);

                        var keyFormat = $"{dt.Year}_{dt.Month}";

                        if (_imgDictionary.Keys.Contains(keyFormat))
                        {
                            _imgDictionary.GetValueOrDefault(keyFormat)?.Add(currentFileInfo.FullName);
                        }
                        else
                        {
                            _imgDictionary.TryAdd(keyFormat, [currentFileInfo.FullName]);
                        }
                    }
                }
                catch (UnknownImageFormatException unknownImageFormatException)
                {
                    Log.Error("Unknown format for: ${ObjFullName}", currentFileInfo.FullName);

                    _imgDictionary.GetValueOrDefault(UnknownFormat)?.Add(currentFileInfo.FullName);

                }
                catch (InvalidImageContentException invalidImageContentException)
                {
                    Log.Error(
                        "Image is not in standard format and is unparsable: ${ObjFullName}", currentFileInfo.FullName);

                    _imgDictionary.GetValueOrDefault(UnknownContent)?.Add(currentFileInfo.FullName);
                }
                catch (IOException ioException)
                {
                    Log.Error("Image is not readable (possibly corrupted): ${ObjFullName}", currentFileInfo.FullName);

                    _imgDictionary.GetValueOrDefault(Unreadable)?.Add(currentFileInfo.FullName);
                }
                catch (NullReferenceException nullReferenceException)
                {
                    Log.Error("Image is not readable (possibly corrupted): ${ObjFullName}", currentFileInfo.FullName);

                    _imgDictionary.GetValueOrDefault(Unreadable)?.Add(currentFileInfo.FullName);
                }
            }
        });
    }
}