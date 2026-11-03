using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jerichofall.View;

/// <summary>The project's authored weapon silhouettes and oil-lamp pixels, shared with its earlier art pack.</summary>
internal static class CombatArt
{
    private static readonly Dictionary<Equipment,Texture2D> weapons=[];
    private static readonly Texture2D?[] lamps=new Texture2D?[2];

    internal static Texture2D Weapon(Equipment kind)
    {
        if(kind>=Equipment.EmberVessel)return RelicArt.Item(kind);
        if(weapons.TryGetValue(kind,out var result))return result;
        string[] mask=kind switch
        {
            Equipment.Jawbone=>["000001100","000012210","011122210","122221100","011211000","001010000","000000000"],
            Equipment.Staff=>["0000000001110","0000000012221","1111111112101","1222222221100","0111111111000"],
            Equipment.Spear=>["000000000001000","000000000012210","111111111122221","022222222212210","000000000001000"],
            _=>["01100","12210","12221","01210","00100"]
        };
        var dark=kind switch{Equipment.Stone=>new Color(109,112,120),Equipment.Jawbone=>new Color(142,133,101),_=>new Color(99,57,33)};
        var light=kind switch{Equipment.Stone=>new Color(211,212,193),Equipment.Jawbone=>new Color(237,227,183),Equipment.Staff=>new Color(185,126,64),_=>new Color(229,186,98)};
        var pixels=mask.SelectMany(row=>row.Select(c=>c=='1'?dark:c=='2'?light:Color.Transparent)).ToArray();
        return weapons[kind]=Canvas2D.Texture(mask[0].Length,mask.Length,pixels);
    }
    internal static Texture2D Lamp(int frame)
    {
        frame%=2;if(lamps[frame] is{} existing)return existing;
        // Authored bronze bracket and two authored flame poses.
        string[] mask=["0000000000","0100000000","0120000000","0111111000","0122222100","0012331000","0001210000","0001210000","0001110000","0000000000"];
        Color[] palette=[Color.Transparent,new(28,25,34),new(195,109,61),new(206,159,75),new(251,220,151)];
        var pixels=mask.SelectMany(row=>row.Select(c=>palette[c-'0'])).ToArray();
        string[] flame=frame==0?["00400","03400","03440","00330"]:["00040","00340","03440","00330"];
        for(int y=0;y<flame.Length;y++)for(int x=0;x<flame[y].Length;x++)if(flame[y][x]!='0')pixels[y*10+x+3]=palette[flame[y][x]-'0'];
        return lamps[frame]=Canvas2D.Texture(10,10,pixels);
    }
    internal static Color Tint(Equipment kind)=>kind switch
    {
        Equipment.Stone=>new(211,212,193),Equipment.Staff=>new(185,126,64),Equipment.Jawbone=>new(237,227,183),Equipment.Spear=>new(229,186,98),_=>RelicArt.Color(kind)
    };
    internal static string Name(Equipment kind)=>kind switch
    {
        Equipment.EmberVessel=>"EMBER",Equipment.BronzeDisc=>"DISC",Equipment.StormJavelin=>"STORM",Equipment.CovenantSeal=>"SEAL",Equipment.WindSandals=>"SANDALS",_=>kind.ToString().ToUpperInvariant()
    };
    internal static void Dispose()
    {
        foreach(var t in weapons.Values)t.Dispose();weapons.Clear();
        for(int i=0;i<lamps.Length;i++){lamps[i]?.Dispose();lamps[i]=null;}
    }
}
