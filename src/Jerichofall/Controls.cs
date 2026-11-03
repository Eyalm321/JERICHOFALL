using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PadButtons=Microsoft.Xna.Framework.Input.Buttons;

namespace Jerichofall.View;

internal sealed class Controls
{
    private KeyboardState keyboard,previous;
    private readonly GamePadState[] pads=new GamePadState[4],oldPads=new GamePadState[4];
    public bool Press(Keys key)=>keyboard.IsKeyDown(key)&&previous.IsKeyUp(key);
    public bool MenuUp=>Press(Keys.Up)||Press(Keys.W)||PadEdge(PadButtons.DPadUp);
    public bool MenuDown=>Press(Keys.Down)||Press(Keys.S)||PadEdge(PadButtons.DPadDown);
    public bool MenuLeft=>Press(Keys.Left)||Press(Keys.A)||PadEdge(PadButtons.DPadLeft);
    public bool MenuRight=>Press(Keys.Right)||Press(Keys.D)||PadEdge(PadButtons.DPadRight);
    public bool Confirm=>Press(Keys.Enter)||Press(Keys.C)||PadEdge(PadButtons.A);
    public bool Back=>Press(Keys.Escape)||PadEdge(PadButtons.B);
    private bool PadEdge(Microsoft.Xna.Framework.Input.Buttons button)=>pads[0].IsButtonDown(button)&&oldPads[0].IsButtonUp(button);
    public void Poll()
    {
        previous=keyboard;keyboard=Keyboard.GetState();
        for(int i=0;i<4;i++){oldPads[i]=pads[i];pads[i]=GamePad.GetState((PlayerIndex)i);}
    }
    public PlayerInput Player(int slot)
    {
        int x=0,y=0;Jerichofall.Buttons buttons=0;
        if(slot<2)
        {
            Keys left=slot==0?Keys.Left:Keys.A,right=slot==0?Keys.Right:Keys.D,up=slot==0?Keys.Up:Keys.W,down=slot==0?Keys.Down:Keys.S;
            x=(keyboard.IsKeyDown(right)?1:0)-(keyboard.IsKeyDown(left)?1:0);y=(keyboard.IsKeyDown(down)?1:0)-(keyboard.IsKeyDown(up)?1:0);
            if(keyboard.IsKeyDown(slot==0?Keys.C:Keys.Space))buttons|=Jerichofall.Buttons.Jump;
            if(keyboard.IsKeyDown(slot==0?Keys.X:Keys.J))buttons|=Jerichofall.Buttons.Fire;
            if(keyboard.IsKeyDown(slot==0?Keys.LeftShift:Keys.K))buttons|=Jerichofall.Buttons.Dodge;
            if(keyboard.IsKeyDown(slot==0?Keys.V:Keys.L))buttons|=Jerichofall.Buttons.Cycle;
        }
        if(slot==0&&keyboard.IsKeyDown(Keys.Z))buttons|=Jerichofall.Buttons.Fire;
        var p=pads[slot];
        if(p.IsConnected)
        {
            int px=Math.Abs(p.ThumbSticks.Left.X)>.3f?Math.Sign(p.ThumbSticks.Left.X):0;
            int py=Math.Abs(p.ThumbSticks.Left.Y)>.3f?-Math.Sign(p.ThumbSticks.Left.Y):0;
            if(p.DPad.Left==ButtonState.Pressed)px=-1;if(p.DPad.Right==ButtonState.Pressed)px=1;if(p.DPad.Up==ButtonState.Pressed)py=-1;if(p.DPad.Down==ButtonState.Pressed)py=1;
            if(px!=0)x=px;if(py!=0)y=py;
            if(p.Buttons.A==ButtonState.Pressed)buttons|=Jerichofall.Buttons.Jump;
            if(p.Buttons.X==ButtonState.Pressed)buttons|=Jerichofall.Buttons.Fire;
            if(p.Buttons.B==ButtonState.Pressed||p.Triggers.Right>.5f)buttons|=Jerichofall.Buttons.Dodge;
            if(p.Buttons.Y==ButtonState.Pressed)buttons|=Jerichofall.Buttons.Cycle;
            if(p.Buttons.Start==ButtonState.Pressed)buttons|=Jerichofall.Buttons.Pause;
        }
        if(slot==0&&keyboard.IsKeyDown(Keys.Escape))buttons|=Jerichofall.Buttons.Pause;
        if(keyboard.IsKeyDown(Keys.Enter)||p.Buttons.A==ButtonState.Pressed)buttons|=Jerichofall.Buttons.Confirm;
        return new(x,y,buttons);
    }
}
