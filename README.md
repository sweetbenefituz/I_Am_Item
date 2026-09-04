# I Am Item

You died. Instead of just watching, press **V** and the game throws you into a
random valuable somewhere on the level. A mug, a toilet, a grand piano. For a
while you are that thing: roll, jump, rattle, block a doorway, and slam into a
monster hard enough to hurt it.

You do not pick the item. That is the whole game. Sometimes you land in a vase
next to the friend being chased. Sometimes you land in a statue in an empty
corridor. Possession is a lottery, not a precision tool.

![Charging up](https://raw.githubusercontent.com/sweetbenefituz/I_Am_Item/main/images/screenshot-1.png)
![Ready to possess](https://raw.githubusercontent.com/sweetbenefituz/I_Am_Item/main/images/screenshot-2.png)

While you are dead, three real valuables from this level spin above the
spectate head, and the prompt counts your bar up to the moment you can jump in.

## The item does not lose money

While a ghost is inside, the valuable **loses no value at all** - not from
falls, not from tumbles, not from being knocked off a shelf. Landing in a
20,000 crystal ball should not tax the team for the crime of moving.

That holds while you ram, too. Speed is the weapon and there is no key for it:
get moving fast enough and whatever you hit takes the damage instead of you.
The risk is everything around the hit - where you end up afterwards, the
teammate you flatten, the other valuable you shatter on the way through.

Deadly pits, lava and crushers are the exception. There the shield does not
help: the item dies, you get ejected, and your bar is wiped.

## Controls

| Key | Action |
|---|---|
| **V** | Possess a random valuable (dead, spectating, bar charged) |
| **WASD** | Roll along the floor |
| **Space** hold and release | Charge a jump. Big items slide instead of jumping |
| **LMB** | Rattle: noise that pulls monsters. If a living player is holding you, one knock |
| **E** | Leave the item |

Team convention, not enforced by the mod: one knock means yes or run, two
knocks mean no or monster behind you.

![Rolling around as loot](https://raw.githubusercontent.com/sweetbenefituz/I_Am_Item/main/images/screenshot-3.png)

## The stability bar

The mod does not add a second resource for getting in. It uses the same energy
the game already spends on rolling your severed head around, so a dead player
has one tank for everything: help the team through an item, or push your head
to the truck and get revived. The Death Head Battery upgrade makes both longer.

Inside the item you have your own stamina. A full bar is about 90 seconds of
doing nothing, and roughly ten jumps if you spend it on jumping. Slamming into
a wall costs a little, but scraping along the floor while rolling is free.
Ramming costs nothing extra - the speed you already paid for is the attack.

## Item classes

| Class | Examples | Movement |
|---|---|---|
| Tiny, Small | mug, flash drive, skulls | Fast roll, high charged jump |
| Medium | figurines, boxes, canisters | Slower, heavier jump |
| Big, Wide, Tall, VeryTall | grand piano, dinosaur, cabinet | No jump. Slides forward on the floor |

Heavy items are not about mobility. They are about noise, about parking
yourself in a doorway, and about hitting very hard when you do get moving.

## Everyone in the lobby needs the mod

The sad line a living player says when they pick you up has to be sent by that
player's own client - the game validates the sender. Without the mod on their
side there is no phrase. Everything else still works.

## Configuration

Every number lives in `BepInEx/config/sweet.iamitem.cfg` and can be edited in a
text editor. Notable switches:

- `RamEnabled = false` - a possessed item hurts nothing, ever.
- `RamHurtsPlayers`, `RamTumblesPlayers` - whether a fast item can flatten a
  living teammate.
- `RamDamage`, `RamMinSpeed`, `RamFullSpeed` - how hard and how fast.
- `PossessKey` - rebind possession away from V.
- `SadPhrasesEnabled`, `CartPhrasesEnabled`, `BreakPhrasesEnabled` - voice lines.
- `PreviewEnabled`, `PreviewCount` - the spinning models above the head.
- `GlowEnabled` - the fresnel glow on a possessed item.

## Building

Copy `src/Local.props.example` to `src/Local.props` and fill in your own paths:
the `REPO_Data\Managed` folder of the installed game, and your Thunderstore
profile name. That file stays out of git.

```
python src/pack.py
```

Runs the balance checks, builds the DLL, verifies the version matches in three
places, packs the Thunderstore archive and drops the files into your profile.

Build the DLL only:

```
cd src && dotnet build -c Release
```

Run the balance checks only:

```
cd tests && dotnet run -c Release
```

## How it works

Half the mechanic already exists in the game, and the mod leans on it instead
of shipping its own:

- head energy and the charged spectate jump (`SpectateCamera`, `PlayerDeathHead`);
- the money shield: the public `destroyDisable` flag, no patch needed;
- forced voice lines (`ChatManager.PossessChat`);
- the glow uses the stock `_FresnelColor` / `_FresnelPower` shader properties
  the game already uses to light up mimics.

No custom models, textures, shaders or sounds. Some of the fields the mod needs
are `internal`, so `BepInEx.AssemblyPublicizer.MSBuild` publicises a copy of the
assembly in `obj`; the game's own files are never touched.

MIT.
