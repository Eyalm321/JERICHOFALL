using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jerichofall.View;

internal static class Canvas2D
{
    public static GraphicsDevice Device { get; set; }=null!;
    public static SpriteBatch SpriteBatch { get; set; }=null!;
    public static Texture2D Pixel { get; set; }=null!;
    public static Texture2D Texture(int width,int height,Color[] pixels)
    {
        var texture=new Texture2D(Device,width,height);texture.SetData(pixels);return texture;
    }
    public static void Rect(float x,float y,float width,float height,Color color)=>SpriteBatch.Draw(Pixel,new Rectangle((int)x,(int)y,(int)width,(int)height),color);
}
