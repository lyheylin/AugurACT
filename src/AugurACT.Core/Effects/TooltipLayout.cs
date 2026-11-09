using System;
using System.Collections.Generic;
using System.Linq;

namespace AugurACT.Core.Effects
{
    // A rectangle in an image, in pixels.
    public sealed class PixelRect
    {
        public PixelRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public override string ToString() => $"{X},{Y} {Width}x{Height}";
    }

    // Finds the icon in a screenshot of an EQ2 effect tooltip: a dark panel with coloured text, and the effect's
    // icon near its top right. The icon is the biggest roughly square patch of bright or colourful pixels that
    // isn't text (the OCR's word boxes) or the panel's frame (long straight lines).
    public static class TooltipLayout
    {
        // pixels are ARGB, row by row. text holds the OCR's word boxes.
        public static PixelRect? FindIcon(int[] pixels, int width, int height, IEnumerable<PixelRect> text)
        {
            if (width < 8 || height < 8 || pixels.Length < width * height)
            {
                return null;
            }

            var busy = new bool[width * height];
            for (var i = 0; i < busy.Length; i++)
            {
                busy[i] = IsBusy(pixels[i]);
            }

            foreach (var box in text)
            {
                Clear(busy, width, height, box.X - 2, box.Y - 2, box.Right + 2, box.Bottom + 2);
            }

            ClearLongRuns(busy, width, height);

            var smallest = Math.Max(12, Math.Min(width, height) / 30);
            var best = Merge(Components(busy, width, height), gap: 3)
                .Where(c => c.Rect.Width >= smallest && c.Rect.Height >= smallest)
                .Where(c => Aspect(c.Rect) <= 1.4)
                .Where(c => c.Fill >= 0.25)
                .Where(c => c.Rect.Width * 2 < width || c.Rect.Height * 2 < height)
                .OrderByDescending(c => Score(c, width, height))
                .FirstOrDefault();
            return best == null ? null : Square(best.Rect, width, height);
        }

        // Bright, or clearly coloured: icon art. The panel behind the text is dark and close to grey.
        private static bool IsBusy(int argb)
        {
            int r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            return max >= 170 || (max >= 80 && max - min >= 50);
        }

        private static void Clear(bool[] busy, int width, int height, int x0, int y0, int x1, int y1)
        {
            for (var y = Math.Max(0, y0); y < Math.Min(height, y1); y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                {
                    busy[y * width + x] = false;
                }
            }
        }

        // The panel's frame and separators: rows or columns of busy pixels far longer than an icon is wide.
        private static void ClearLongRuns(bool[] busy, int width, int height)
        {
            var longest = Math.Max(60, Math.Min(width, height) / 3);
            var clear = new List<int>();
            for (var y = 0; y < height; y++)
            {
                for (int x = 0, run = 0; x <= width; x++)
                {
                    if (x < width && busy[y * width + x])
                    {
                        run++;
                        continue;
                    }

                    if (run > longest)
                    {
                        clear.AddRange(Enumerable.Range(x - run, run).Select(i => y * width + i));
                    }

                    run = 0;
                }
            }

            for (var x = 0; x < width; x++)
            {
                for (int y = 0, run = 0; y <= height; y++)
                {
                    if (y < height && busy[y * width + x])
                    {
                        run++;
                        continue;
                    }

                    if (run > longest)
                    {
                        clear.AddRange(Enumerable.Range(y - run, run).Select(i => i * width + x));
                    }

                    run = 0;
                }
            }

            foreach (var i in clear)
            {
                busy[i] = false;
            }
        }

        private sealed class Blob
        {
            public Blob(PixelRect rect, int count)
            {
                Rect = rect;
                Count = count;
            }

            public PixelRect Rect { get; }

            public int Count { get; }

            public double Fill => (double)Count / (Rect.Width * Rect.Height);
        }

        private static List<Blob> Components(bool[] busy, int width, int height)
        {
            var seen = new bool[busy.Length];
            var blobs = new List<Blob>();
            var stack = new Stack<int>();
            for (var start = 0; start < busy.Length; start++)
            {
                if (!busy[start] || seen[start])
                {
                    continue;
                }

                int x0 = width, y0 = height, x1 = -1, y1 = -1, count = 0;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    int x = i % width, y = i / width;
                    count++;
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            {
                                continue;
                            }

                            var n = ny * width + nx;
                            if (busy[n] && !seen[n])
                            {
                                seen[n] = true;
                                stack.Push(n);
                            }
                        }
                    }
                }

                blobs.Add(new Blob(new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), count));
            }

            return blobs;
        }

        // Icon art has dark patches that split it into pieces; join pieces whose boxes touch or nearly do.
        private static List<Blob> Merge(List<Blob> blobs, int gap)
        {
            var merged = true;
            while (merged)
            {
                merged = false;
                for (var i = 0; i < blobs.Count && !merged; i++)
                {
                    for (var j = i + 1; j < blobs.Count; j++)
                    {
                        PixelRect a = blobs[i].Rect, b = blobs[j].Rect;
                        if (a.X - gap > b.Right || b.X - gap > a.Right || a.Y - gap > b.Bottom || b.Y - gap > a.Bottom)
                        {
                            continue;
                        }

                        int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
                        var rect = new PixelRect(x0, y0, Math.Max(a.Right, b.Right) - x0, Math.Max(a.Bottom, b.Bottom) - y0);
                        blobs[i] = new Blob(rect, blobs[i].Count + blobs[j].Count);
                        blobs.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }

            return blobs;
        }

        private static double Aspect(PixelRect r) => (double)Math.Max(r.Width, r.Height) / Math.Min(r.Width, r.Height);

        // Bigger is better, and the tooltip puts the icon at its top right.
        private static double Score(Blob blob, int width, int height)
        {
            var r = blob.Rect;
            var score = (double)r.Width * r.Height;
            if (r.X + r.Width / 2 > width / 2)
            {
                score *= 1.5;
            }

            if (r.Y + r.Height / 2 < height / 2)
            {
                score *= 1.5;
            }

            return score;
        }

        // The icon is square; grow the short side around the centre, inside the image.
        private static PixelRect Square(PixelRect r, int width, int height)
        {
            var side = Math.Min(Math.Max(r.Width, r.Height), Math.Min(width, height));
            var x = Math.Max(0, Math.Min(width - side, r.X + r.Width / 2 - side / 2));
            var y = Math.Max(0, Math.Min(height - side, r.Y + r.Height / 2 - side / 2));
            return new PixelRect(x, y, side, side);
        }
    }
}
