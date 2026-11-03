# Playing JERICHOFALL

Linux: `bash tools/play.sh` from source, or `bash play.sh` in an extracted portable build. Windows: double-click `Play.cmd`. The independent game needs no other installed game.

Use arrows and Enter/C to select a menu. In hero selection, Left/Right chooses player one, Q/E chooses player two, Tab changes player count, and F7 cycles Versus arenas. Campaign supports one or two local players; Versus supports two to four. Players three/four use controllers and default to Samson/Joshua. Online joiners choose a hero with F7 on the join screen.

On the Exodus map: arrows select a checkpoint, Enter opens its briefing, Enter starts, H opens hero selection, Escape returns. Each checkpoint has three encounters. Clear it to unlock the next. N offers a new journey and archives the existing save after confirmation.

| Action | Keyboard P1 | Keyboard P2 | Controller |
| --- | --- | --- | --- |
| Move / aim | Arrows | WASD | Left stick / D-pad |
| Jump | C | Space | A |
| Hold aim, release fire | X or Z | J | X |
| Dodge / catch | Left Shift | K | B / Right trigger |
| Cycle ammunition | V | L | Y |
| Pause | Escape | — | Start |

F11 toggles fullscreen; M toggles sound. Paused or completed battle: Backspace returns to the menu. Enter retries after defeat or continues after a clear; after a Versus round, Enter starts the next round/rematch. Versus is first to five round wins. Mid-match reconnection is not supported.

After a Versus death, your spirit rises from the body after about a second. Use your usual movement controls to float, and Fire or Dodge to dash. After its short emergence/grace period, touching another player can eliminate them. Survivors can banish spirits with a projectile or a stomp. The last survivor gets a bounded extra revenge window before the round ends. Campaign deaths show a harmless rising spirit; your existing lives and respawn rules still apply. Pause freezes spirits too.

Campaign saves use `.NET LocalApplicationData/JERICHOFALL/standalone-campaign.json` (normally `~/.local/share/JERICHOFALL/` on Linux or the Windows local application-data folder). Backups and preserved corrupt saves sit alongside it. `--save PATH` overrides the location. To retain old Exodus progression, copy its `exodus-progress.json` to a new path and pass that copy with `--save`; version 1/2 progression formats are supported. Engine settings and scores are not imported.

## Online Versus

This alpha's direct TCP connection is unauthenticated and unencrypted. Host for
trusted peers; it is not a public server service.

Choose Host Online Versus, select hero/player count/arena, and confirm. Guests choose Join Online Versus and enter `host-address:28060`. Every player needs the same independent release. Host must be reachable on TCP 28060; there is no relay, NAT traversal or matchmaking. The host simulates; guests render snapshots. Network delay affects response time because this alpha has no client prediction.

Equivalent source-launch commands:

```bash
bash tools/play.sh --host 28060 --players 4 --arena jerusalem --hero 0
bash tools/play.sh --join 192.168.1.10:28060 --hero 2
```

Heroes: 0 David, 1 Moses, 2 Samson, 3 Joshua. Arenas: `jerusalem`, `carmel`, `negev`. Host selects two to four players. Campaign is local co-op.
