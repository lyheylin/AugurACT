using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AugurACT.Core.Mechanics;

namespace AugurACT.Core.Effects
{
    // The folder of screenshots and icons for buffs and detrimentals, next to the encounters data. Files are PNGs
    // named by a hash of their bytes, so the same image saved twice is one file, and a record only holds the name.
    // The page shows them from https://{VirtualHost}/{name}.
    public sealed class EffectImages
    {
        public const string VirtualHost = "augur-images.act";

        private static readonly Regex NamePattern = new Regex("^[0-9a-f]{32}\\.png$", RegexOptions.CultureInvariant);

        public EffectImages(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        // Only names this class makes, so a name from the page can't point outside the folder.
        public static bool IsValidName(string? name) => name != null && NamePattern.IsMatch(name);

        // png is the image as PNG bytes. Saving one already there only marks it as new again, for Prune.
        public string Save(byte[] png)
        {
            string name;
            using (var sha = SHA256.Create())
            {
                name = string.Concat(sha.ComputeHash(png).Take(16).Select(b => b.ToString("x2"))) + ".png";
            }

            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, name);
            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            else
            {
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, png);
                File.Move(temp, path);
            }

            return name;
        }

        // Deletes images no record names, once they're older than the cutoff: an image is saved as soon as it's
        // added to the dialog, before the effect itself is, and may be dropped from it again. Returns how many.
        public int Prune(EncounterStore store, DateTime cutoffUtc)
        {
            if (!Directory.Exists(Folder))
            {
                return 0;
            }

            var inUse = store.ImagesInUse();
            var removed = 0;
            foreach (var path in Directory.GetFiles(Folder, "*.png"))
            {
                var name = Path.GetFileName(path);
                if (IsValidName(name) && !inUse.Contains(name) && File.GetLastWriteTimeUtc(path) < cutoffUtc)
                {
                    File.Delete(path);
                    removed++;
                }
            }

            return removed;
        }
    }
}
