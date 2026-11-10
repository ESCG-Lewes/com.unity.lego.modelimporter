using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace LEGOModelImporter
{
    internal static class StudioPartBundles
    {
        internal static readonly string GeometryPath = PartUtility.geometryPath + "/Studio";
        private static readonly Regex ArchiveName = new Regex(@"\AStudioParts-(Animals|Props|Shapes|Minifigs)-[0-9]+\.zip\z");
        private static readonly Regex PartName = new Regex(@"\A[a-z0-9_.+-]+\.dat\z");
        private static readonly Regex Revision = new Regex(@"\A[0-9a-f]{16}\z");

        private static Dictionary<string, (string archive, string revision)> entries;

        internal static bool Contains(string name)
        {
            if (entries == null)
            {
                entries = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                var manifest = Path.Combine(PartUtility.packageDataPath, "StudioParts.tsv");
                if (File.Exists(manifest))
                {
                    foreach (var line in File.ReadLines(manifest))
                    {
                        var fields = line.Split('\t');
                        if (fields.Length >= 3 && PartName.IsMatch(fields[0]) &&
                            ArchiveName.IsMatch(fields[1]) && Revision.IsMatch(fields[2]))
                        {
                            entries[fields[0]] = (fields[1], fields[2]);
                        }
                    }
                }
            }

            return entries.ContainsKey(name);
        }

        internal static XmlDocument LoadConnections(string name)
        {
            var path = Path.Combine(PartUtility.packageDataPath, "StudioConnections.zip");
            if (!Contains(name) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var archive = ZipFile.OpenRead(path))
                {
                    var entry = archive.GetEntry(Path.ChangeExtension(name.ToLowerInvariant(), ".xml"));
                    if (entry == null)
                    {
                        return null;
                    }

                    using (var stream = entry.Open())
                    {
                        var document = new XmlDocument();
                        document.Load(stream);
                        return document;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                               exception is UnauthorizedAccessException || exception is XmlException)
            {
                Debug.LogWarning($"Could not read Studio connections for {name}. {exception.Message}");
                return null;
            }
        }

        internal static GameObject Load(string name, bool lightmapped)
        {
            if (!Contains(name))
            {
                return null;
            }

            var (archiveName, revision) = entries[name];
            var filename = Path.ChangeExtension(name.ToLowerInvariant(), ".fbx");
            var folder = lightmapped ? GeometryPath + "/Lightmapped/" : GeometryPath + "/";
            var path = folder + revision + "/" + filename;
            if (File.Exists(path))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            try
            {
                using (var archive = ZipFile.OpenRead(Path.Combine(PartUtility.packageDataPath, archiveName)))
                {
                    var entry = archive.GetEntry(filename);
                    if (entry == null)
                    {
                        throw new InvalidDataException(filename + " is missing from " + archiveName);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var temporaryPath = path + ".tmp";
                    try
                    {
                        entry.ExtractToFile(temporaryPath, true);
                        File.Move(temporaryPath, path);
                    }
                    finally
                    {
                        File.Delete(temporaryPath);
                    }
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                               exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not unpack Studio part {name}. {exception.Message}");
                return null;
            }
        }
    }
}
