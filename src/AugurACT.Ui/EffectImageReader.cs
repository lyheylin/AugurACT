using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AugurACT.Core.Effects;
using AugurACT.Core.Host;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AugurACT.Ui
{
    // Handles "add-effect-image": saves a screenshot or icon for a buff or detrimental, and from a tooltip
    // screenshot cuts out the icon (TooltipLayout) and reads the text with Windows' own OCR (TooltipText).
    // Blocks while it reads, so it runs on the plugin's worker thread, not the UI thread.
    public static class EffectImageReader
    {
        // Bigger than any screen; anything larger is not a screenshot.
        private const int MaxPixels = 40_000_000;

        public static EffectImageMessage Add(EffectImages images, string? requestId, string? data, bool asIcon)
        {
            var message = new EffectImageMessage { RequestId = requestId };
            Bitmap bitmap;
            try
            {
                bitmap = Decode(data);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is ExternalException)
            {
                message.Error = "That file isn't an image AugurACT can read.";
                return message;
            }

            using (bitmap)
            {
                if ((long)bitmap.Width * bitmap.Height > MaxPixels)
                {
                    message.Error = "That image is too big.";
                    return message;
                }

                message.File = images.Save(Png(bitmap));
                if (asIcon)
                {
                    return message;
                }

                var pixels = Pixels(bitmap);
                var words = new List<PixelRect>();
                try
                {
                    var lines = Read(bitmap, pixels, words);
                    if (lines == null)
                    {
                        message.Warning = "Windows has no OCR language installed, so the text wasn't read. Add English in Windows' language settings.";
                    }
                    else
                    {
                        var text = TooltipText.Read(lines);
                        message.Name = Blank(text.Name);
                        message.Description = Blank(text.Description);
                    }
                }
                catch (Exception ex) when (ex is COMException || ex is TypeLoadException || ex is FileNotFoundException || ex is InvalidOperationException)
                {
                    message.Warning = "The text couldn't be read: " + ex.Message;
                }

                var icon = TooltipLayout.FindIcon(pixels, bitmap.Width, bitmap.Height, words);
                if (icon != null)
                {
                    using (var crop = bitmap.Clone(new Rectangle(icon.X, icon.Y, icon.Width, icon.Height), PixelFormat.Format32bppArgb))
                    {
                        message.Icon = images.Save(Png(crop));
                    }
                }
            }

            return message;
        }

        private static Bitmap Decode(string? data)
        {
            var text = data ?? throw new ArgumentException("No image.");
            var comma = text.IndexOf(',');
            if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            {
                text = text.Substring(comma + 1);
            }

            using (var stream = new MemoryStream(Convert.FromBase64String(text)))
            using (var image = Image.FromStream(stream))
            {
                // A copy in one known format, independent of the stream.
                var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.DrawImage(image, 0, 0, image.Width, image.Height);
                }

                return bitmap;
            }
        }

        private static byte[] Png(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static int[] Pixels(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[bitmap.Width * bitmap.Height];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
                }

                return pixels;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        // Reads the whole tooltip once to find its lines, then each line again on its own at a few sizes and keeps
        // the most plausible reading (TooltipText.Plausibility): Windows' OCR drops and garbles words it isn't sure
        // of, differently at each size. Fills words
        // with the word boxes, in the bitmap's pixels. Null when Windows has no OCR language.
        private static List<TooltipLine>? Read(Bitmap bitmap, int[] pixels, List<PixelRect> words)
        {
            var engine = Engine();
            if (engine == null)
            {
                return null;
            }

            const int Scale = 2;
            var whole = Recognize(engine, Prepare(bitmap, pixels, new Rectangle(0, 0, bitmap.Width, bitmap.Height), Scale));

            // Word boxes in the bitmap's pixels, per line.
            var found = whole.Lines
                .Select(l => (l.Text, Boxes: l.Words.Select(w => new RectangleF(
                    (float)((w.BoundingRect.X - Pad) / Scale), (float)((w.BoundingRect.Y - Pad) / Scale),
                    (float)(w.BoundingRect.Width / Scale), (float)(w.BoundingRect.Height / Scale))).ToList()))
                .Where(l => l.Boxes.Count > 0)
                .ToList();
            var usual = Median(found.Select(l => (double)l.Boxes.Average(b => b.Height)));

            var lines = new List<TooltipLine>();
            foreach (var (text, boxes) in found)
            {
                foreach (var b in boxes)
                {
                    words.Add(new PixelRect((int)b.X, (int)b.Y, (int)Math.Ceiling(b.Width) + 1, (int)Math.Ceiling(b.Height) + 1));
                }

                var height = boxes.Average(b => b.Height);
                var best = text;

                // Read the line again on its own, and only its own: a band across the whole tooltip catches the
                // icon, and a "line" much taller than the rest (a column of bullets) isn't one.
                var margin = (int)Math.Ceiling(height * 2);
                var left = Math.Max(0, (int)boxes.Min(b => b.X) - margin);
                var right = Math.Min(bitmap.Width, (int)Math.Ceiling(boxes.Max(b => b.Right)) + margin);
                var top = Math.Max(0, (int)boxes.Min(b => b.Y) - 3);
                var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(boxes.Max(b => b.Bottom)) + 3);
                if (height <= usual * 2.5 && right > left && bottom > top)
                {
                    var band = new Rectangle(left, top, right - left, bottom - top);
                    foreach (var scale in new[] { 2, 3, 4 })
                    {
                        var centre = Pad + band.Height * scale / 2.0;
                        var again = Recognize(engine, Prepare(bitmap, pixels, band, scale)).Lines
                            .Where(l => l.Words.Count > 0)
                            .OrderBy(l => Math.Abs(l.Words.Average(w => w.BoundingRect.Y + w.BoundingRect.Height / 2) - centre))
                            .FirstOrDefault()?.Text ?? "";
                        if (TooltipText.Plausibility(again) > TooltipText.Plausibility(best))
                        {
                            best = again;
                        }
                    }
                }

                lines.Add(new TooltipLine(best, height));
            }

            return lines;
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
        }

        private static OcrEngine? Engine()
        {
            var english = new Language("en-US");
            return OcrEngine.IsLanguageSupported(english)
                ? OcrEngine.TryCreateFromLanguage(english)
                : OcrEngine.TryCreateFromUserProfileLanguages();
        }

        private const int Pad = 20;

        // The OCR reads dark text on light best: scale up, take each pixel's brightest channel (so coloured text
        // is as strong as white), and invert. A margin keeps text off the edge.
        private static byte[] Prepare(Bitmap bitmap, int[] pixels, Rectangle area, int scale)
        {
            using (var region = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                var data = region.LockBits(new Rectangle(0, 0, area.Width, area.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[area.Width];
                    for (var y = 0; y < area.Height; y++)
                    {
                        for (var x = 0; x < area.Width; x++)
                        {
                            var p = pixels[(area.Y + y) * bitmap.Width + area.X + x];
                            var v = 255 - Math.Max((p >> 16) & 0xFF, Math.Max((p >> 8) & 0xFF, p & 0xFF));
                            row[x] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
                        }

                        Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, area.Width);
                    }
                }
                finally
                {
                    region.UnlockBits(data);
                }

                using (var big = new Bitmap(area.Width * scale + Pad * 2, area.Height * scale + Pad * 2, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(big))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(region, new Rectangle(Pad, Pad, area.Width * scale, area.Height * scale));
                    }

                    return Png(big);
                }
            }
        }

        private static OcrResult Recognize(OcrEngine engine, byte[] png)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(png);
                    writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                    writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                    writer.DetachStream();
                }

                stream.Seek(0);
                var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                using (var image = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult())
                {
                    return engine.RecognizeAsync(image).AsTask().GetAwaiter().GetResult();
                }
            }
        }

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
