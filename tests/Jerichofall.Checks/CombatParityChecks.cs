using System.Numerics;
using Jerichofall;

internal static class CombatParityChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string reason)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Combat parity: " + reason);
        }
        void Near(float actual, float expected, string reason) => Check(MathF.Abs(actual - expected) < .00002f, reason);

        // These samples were recorded from one native Update per tick, not generated
        // from the new controller. Position integration has a separate parity suite.
        float[] stoneX = [5.5832f, 5.56645f, 5.549751f, 5.5331016f, 5.516502f,
            5.4999523f, 5.4834523f, 5.467002f, 5.4506006f, 5.434249f];
        float[] stoneY = [.11f, .22f, .32999998f, .44f, .55f, .66f, .77000004f, .88000005f, .99000007f, 1.1f];
        Vector2 velocity = CombatPhysics.LaunchVelocity(Equipment.Stone, 0);
        float direction = 0;
        for (int frame = 0; frame < stoneX.Length; frame++)
        {
            var step = CombatPhysics.AdvanceFlight(Equipment.Stone, velocity, frame + 1, direction);
            velocity = step.Velocity; direction = step.Direction;
            Near(velocity.X, stoneX[frame], "measured stone horizontal flight tick " + (frame + 1));
            Near(velocity.Y, stoneY[frame], "measured stone vertical flight tick " + (frame + 1));
        }
        // Independently authored legacy weapon profiles have distinct first-tick paths.
        foreach (var sample in new (Equipment Kind, float X, float Y)[]
        {
            (Equipment.Staff, 4.7664f, .14f), (Equipment.Jawbone, 4.2484f, .19f),
            (Equipment.Spear, 6.3936f, .04f), (Equipment.EmberVessel, 4.4505f, -.74f),
            (Equipment.BronzeDisc, 5.6f, 0), (Equipment.StormJavelin, 8, .012f)
        })
        {
            var step = CombatPhysics.AdvanceFlight(sample.Kind, CombatPhysics.LaunchVelocity(sample.Kind, 0), 1, 0);
            Near(step.Velocity.X, sample.X, sample.Kind + " first-tick horizontal flight");
            Near(step.Velocity.Y, sample.Y, sample.Kind + " first-tick vertical flight");
        }
        var jawbone = CombatPhysics.HitTerrain(Equipment.Jawbone, new(4, 2), true, 1);
        Near(jawbone.Velocity.X, -2.88f, "jawbone rebounds with retained momentum");
        Near(jawbone.Velocity.Y, 1.5f, "jawbone wall contact damps vertical momentum");
        Check(!jawbone.Falling && !CombatPhysics.HitTerrain(Equipment.Jawbone, new(4, 2), true, 2).Falling,
            "jawbone stays dangerous through two ricochets");
        Check(CombatPhysics.HitTerrain(Equipment.Jawbone, new(4, 2), true, 3).Falling,
            "jawbone third contact changes to tumbling");
        Check(!CombatPhysics.HitTerrain(Equipment.Staff, new(4, 2), true, 1).Falling &&
            CombatPhysics.HitTerrain(Equipment.Staff, new(4, 2), true, 2).Falling, "staff has one dangerous ricochet");
        Check(CombatPhysics.HitTerrain(Equipment.Stone, new(4, 2), true, 1).Falling &&
            CombatPhysics.HitTerrain(Equipment.Spear, new(4, 2), true, 1).Falling, "stone and spear tumble on first wall contact");
        Check(CombatPhysics.HitTerrain(Equipment.Stone, new(1, .8f), false, 2).Resting, "slow falling weapons settle on the ground");
        Check(!CombatPhysics.AdvanceFlight(Equipment.EmberVessel, new(1, 1), 53, 0).Detonate &&
            CombatPhysics.AdvanceFlight(Equipment.EmberVessel, new(1, 1), 54, 0).Detonate, "ember fuse expires at tick 54");
        Check(!CombatPhysics.AdvanceFlight(Equipment.BronzeDisc, new(5.6f, 0), 18, 0).Returning &&
            CombatPhysics.AdvanceFlight(Equipment.BronzeDisc, new(5.6f, 0), 19, 0).Returning, "disc return starts at tick 19");
        Near(CombatPhysics.AdvanceFlight(Equipment.StormJavelin, new(0, 8), 1, 0).Velocity.Y, 8.012f,
            "downward storm javelin keeps its launch speed");
        Check(CombatPhysics.AdvanceArrow(new(4, 0), 25, 0).Velocity == new Vector2(4, 0) &&
            CombatPhysics.AdvanceArrow(new(4, 0), 26, 0).Velocity == new Vector2(3.97f, .2f), "raider arrows begin gravity after 25 straight ticks");

        var items = Match(); var player = items.State.Fighters[0];
        int lives = player.Lives;
        player.Ammo = [Equipment.BronzeDisc, Equipment.StormJavelin]; player.Invincible = 0;
        Check(items.ApplySupply(player, Equipment.Manna), "manna is accepted");
        Check(player.Ammo.Count == 5 && player.Ammo.Count(a => a == Equipment.Stone) == 3 && player.Lives == lives,
            "manna adds three base rounds without adding lives");
        Check(player.Invincible == 60, "manna grants sixty unprotected ticks");
        player.Invincible = 17; items.ApplySupply(player, Equipment.Manna);
        Check(player.Invincible == 17 && player.Ammo.Count == 6, "manna respects capacity and existing protection");
        Check(items.ApplySupply(player, Equipment.CovenantSeal), "first seal is accepted");
        Check(!items.ApplySupply(player, Equipment.CovenantSeal), "unused complete seal rejects redundant pickup");
        player.Invincible = 0; items.Damage(player);
        Check(player.Alive && player.Shield == 0 && player.Invincible == 30, "seal absorbs hit and grants thirty protection ticks");
        Advance(items, 44);
        Check(player.Shield == 0, "reserve ward remains unavailable through 44 ticks");
        Advance(items, 1);
        Check(player.Shield == 1, "reserve ward returns at 45 ticks");
        player.Invincible = 0; items.Damage(player); Advance(items, 100);
        Check(player.Alive && player.Shield == 0, "seal has only one reserve ward");
        items.ApplySupply(player, Equipment.WindSandals);
        Check(player.Sandals == 600, "sandals last six hundred simulation ticks");
        Advance(items, 600);
        Check(player.Sandals == 0, "sandals expire");

        var discGame = Match(); var discOwner = discGame.State.Fighters[0];
        discOwner.Ammo = Enumerable.Repeat(Equipment.Stone, 6).ToList();
        discGame.Launch(discOwner, Equipment.BronzeDisc, 0);
        var disc = discGame.State.Missiles[^1];
        disc.Position = discOwner.Center + new Vector2(7, 0); disc.Velocity = Vector2.Zero; disc.Age = 18;
        Advance(discGame, 3);
        Check(!disc.Removed && discOwner.Ammo.Count == 6, "full inventory cannot silently consume a returning disc");
        discOwner.Ammo.RemoveAt(0); Advance(discGame, 10);
        Check(disc.Removed && discOwner.Ammo.Count == 6 && discOwner.Ammo.Contains(Equipment.BronzeDisc),
            "returning disc is recovered when space becomes available");

        var blast = Match(wall: 11); var bomber = blast.State.Fighters[0]; var sheltered = blast.State.Fighters[1];
        bomber.Position = new(92, 230); sheltered.Position = new(122, 230);
        blast.Launch(bomber, Equipment.EmberVessel, 0);
        var vessel = blast.State.Missiles[^1]; vessel.Position = new(105, 221); vessel.Velocity = Vector2.Zero; vessel.Age = 53;
        Advance(blast, 4);
        Check(!bomber.Alive, "ember blast can hit its thrower");
        Check(sheltered.Alive, "solid terrain blocks ember damage");

        var storm = Match(); var shooters = storm.State.Fighters;
        shooters[0].Position = new(50, 230); shooters[1].Position = new(100, 230);
        shooters[2].Position = new(117, 230); shooters[3].Position = new(130, 230);
        storm.Launch(shooters[0], Equipment.StormJavelin, 0); Advance(storm, 20);
        Check(!shooters[1].Alive && !shooters[2].Alive && shooters[3].Alive && shooters[0].Alive,
            "storm hits its direct target and only the nearest additional target");
        var blockedStorm = Match(wall: 11); var blocked = blockedStorm.State.Fighters;
        blocked[0].Position = new(50, 230); blocked[1].Position = new(100, 230); blocked[2].Position = new(124, 230);
        blockedStorm.Launch(blocked[0], Equipment.StormJavelin, 0); Advance(blockedStorm, 20);
        Check(!blocked[1].Alive && blocked[2].Alive, "solid terrain blocks storm chaining");

        var enemies = Match(); var guard = EnemyController.Spawn(900, EnemyKind.Guard, new(109, 230));
        enemies.State.Fighters[0].Position = new(100, 230); enemies.State.Fighters.Add(guard);
        Advance(enemies, 1);
        Check(enemies.State.Fighters[0].Alive, "patrolling guard contact does not kill");
        guard.Brain.Phase = EnemyPhase.Attack; guard.Brain.PhaseRemaining = 20; guard.Velocity = new(-2.35f, 0);
        enemies.State.Fighters[0].Position = new(100, 230); enemies.State.Fighters[0].Invincible = 0; guard.Position = new(109, 230);
        Advance(enemies, 1);
        Check(!enemies.State.Fighters[0].Alive, "attacking guard contact is dangerous");
        var captain = EnemyController.Spawn(901, EnemyKind.Captain, new(200, 230)); enemies.State.Fighters.Add(captain);
        enemies.Damage(captain);
        Check(captain.Health == 2 && captain.Brain.Phase == EnemyPhase.Stunned && captain.Invincible == 22,
            "armored enemy hit enters the authored stun and protection interval");
        Check(EnemyController.Spawn(902, EnemyKind.Sentinel, new(200, 230)).Health == 5, "sentinel retains five-hit health");
        var campaignArena=Match().Layout;campaignArena.Portals=[new(160,100)];
        var campaign=new Battle(campaignArena,[Hero.David],BattleMode.Campaign,0,
            [[EnemyKind.Guard,EnemyKind.Guard],[EnemyKind.Scorpion],[EnemyKind.Raider]]);
        campaign.State.Fighters[0].Invincible=10000;
        Check(campaign.State.Fighters[0].Shield==1,"campaign spawns with its original starting ward");
        Advance(campaign,99);Check(campaign.State.Arrivals.Count==0,"campaign introduction lasts one hundred ticks");
        Advance(campaign,1);Check(campaign.State.Arrivals.Count==2&&!campaign.State.Fighters.Any(p=>!p.IsPlayer),"wave queues arrivals before spawning foes");
        Advance(campaign,44);Check(!campaign.State.Fighters.Any(p=>!p.IsPlayer),"portal provides forty-five warning ticks");
        Advance(campaign,1);Check(campaign.State.Fighters.Count(p=>!p.IsPlayer)==1,"first foe arrives after portal warning");
        Advance(campaign,41);Check(campaign.State.Fighters.Count(p=>!p.IsPlayer)==1,"same-portal arrivals remain staggered");
        Advance(campaign,1);Check(campaign.State.Fighters.Count(p=>!p.IsPlayer)==2,"second foe follows forty-two ticks later");
        foreach(var foe in campaign.State.Fighters.Where(p=>!p.IsPlayer).ToArray()){foe.Invincible=0;campaign.Damage(foe);}
        Advance(campaign,19);Check(campaign.State.Phase==BattlePhase.Fighting,"wave clear waits twenty ticks");
        Advance(campaign,1);Check(campaign.State.Phase==BattlePhase.Intermission&&campaign.State.Delay==110,"next wave has one-hundred-ten tick intermission");
        var supplyGame=Match();supplyGame.State.Supplies.Clear();
        var supply=new Supply{Id=999,Kind=Equipment.WindSandals,Position=supplyGame.State.Fighters[0].Center};
        supplyGame.State.Supplies.Add(supply);Advance(supplyGame,11);
        Check(supplyGame.State.Fighters[0].Sandals==0&&!supply.Removed,"supplies cannot be collected during initial unpack");
        Advance(supplyGame,1);Check(supply.Removed&&supplyGame.State.Fighters[0].Sandals==600,"supplies become collectible at twelve ticks");
        return checks;
    }

    private static Battle Match(int wall = -1)
    {
        var arena = new ArenaLayout
        {
            Id = "combat-parity", Title = "COMBAT PARITY",
            Solids = Enumerable.Range(0, 24).Select(y => new string(Enumerable.Range(0, 32).Select(x => y == 23 || x == wall ? '1' : '0').ToArray())).ToArray(),
            Background = Enumerable.Repeat(new string('0', 32), 24).ToArray(),
            Spawns = [new(40, 230), new(140, 230), new(220, 230), new(280, 230)]
        };
        var battle = new Battle(arena, [Hero.David, Hero.Moses, Hero.Samson, Hero.Joshua], BattleMode.Versus);
        battle.State.Phase = BattlePhase.Fighting;
        foreach (var player in battle.State.Fighters) player.Invincible = 0;
        return battle;
    }

    private static void Advance(Battle battle, int frames)
    {
        for (int frame = 0; frame < frames; frame++) battle.Step([default, default, default, default]);
    }
}
