using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using OpenTK.Graphics.OpenGL4;
using System.Runtime.InteropServices;

namespace VectorMap.Core.Rendering;

/// <summary>
/// Manages a texture atlas for font glyphs
/// </summary>
public class FontAtlas : IDisposable
{
    public int TextureId { get; private set; }
    public Dictionary<char, GlyphInfo> Glyphs { get; } = new();
    public int Width { get; } = 512;
    public int Height { get; } = 512;

    public FontAtlas()
    {
        Generate();
    }

    private void Generate()
    {
        using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var font = new Font("Arial", 32, FontStyle.Bold);
        var brush = Brushes.White;

        float curX = 0, curY = 0;
        float rowHeight = 0;

        // Common characters
        string chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        foreach (char c in chars)
        {
            var size = g.MeasureString(c.ToString(), font);
            if (curX + size.Width > Width)
            {
                curX = 0;
                curY += rowHeight;
                rowHeight = 0;
            }

            g.DrawString(c.ToString(), font, brush, curX, curY);

            Glyphs[c] = new GlyphInfo
            {
                U1 = curX / Width,
                V1 = curY / Height,
                U2 = (curX + size.Width) / Width,
                V2 = (curY + size.Height) / Height,
                Width = size.Width,
                Height = size.Height
            };

            curX += size.Width;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        TextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, TextureId);
        
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Width, Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
        bitmap.UnlockBits(data);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    public void Dispose()
    {
        if (TextureId != 0) GL.DeleteTexture(TextureId);
    }
}

public struct GlyphInfo
{
    public float U1, V1, U2, V2;
    public float Width, Height;
}
