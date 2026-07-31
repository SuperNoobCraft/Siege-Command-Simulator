# Siege Command Simulator
---
Voiceover Trailer: https://youtu.be/emaOAygFlzM

Cinematic Trailer: https://youtu.be/jxcJ2IsbopQ
---

## Introduction

### Background

Siege Command Simulator is a game created by Jim Tze Lau under HKU Visioneers during July 2026. It is designed to be played in the Cave Automatic Virtual Environment (CAVE), tested on DASECave and LEDCave located in the Human-System Interaction Simulation (HIS) Lab of the University of Hong Kong. It is developed with Unity 2021.3.452c1 via the VotanicXR plugin.

### Brief Overview
Siege Command Simulator is a CAVE-based immersive command simulator where players direct troops while physically dodging incoming attacks from the battlefield. The player stands inside the CAVE on a virtual platform overseeing the battlefield. The main goal of the player is to command your troops to reposition such that you can defend your cannons from the defenders of the siege until your cannons are ready to be fired. If the cannons survive until the countdown ends, the player wins. If the enemy troops destroy the cannons, the player loses. 

---

## Gameplay Mechanics

### Troop Commanding
Inside of the CAVE, the player can point their wand at their troops, a bright ray indicates the wand, and the troops would be highlighted when they are detected by the ray. Once the player is hovering over a troop, hold down the "A" Button to draw a line, the troops will then follow the line until they reach an obstacle or arrive at the end of the line. The line can be in any shape, but to reduce the impact of analog noise of wand tracking, the turns on the lines are smoothed out to ensure the paths are as the player intended.

### Troop Combat, Retreat, and Regroup
There are currently 2 types of troops, Infantry and Archer. Infantry is the standard melee troop, only being able to attack when directly overlapping with enemy, while Archers can attack from a range safely. To compensate for this, archers have lower movement speed, lower hitpoints, and if the enemies are too close, they resort to melee combat, which deals less damage. Troops automatically enter combat with enemies within their range. If a troop is in melee combat, their movement speed will be reduced. The greater the area of overlap is with the enemy, the slower they would be.

Once a regiment is defeated, they would attempt to retreat to their base. During retreat, they automatically take the shortest path, move faster than normal, and cannot engage in combat. If a regiment is cut off during retreat by an enemy regiment via melee combat, the retreating regiment would be eliminated permanently.

If a regiment returns to camp, either through forced retreat or as ordered by the player, they would begin to regroup and regain hitpoints. Troops do not regain hitpoints outside of camp.

### Enemy Waves

The defenders would send out their troops in waves. For each individual wave, they would have fewer regiments than the player, but if an enemy regiment is defeated and successfully retreats and regroups, they would come out again during the next wave. After the final wave has fired, all enemy troops would come out immediately instead of waiting. If all of the regiments successfully retreats every time, eventually the player would be outnumbered and overwhelmed, hence it is advised to prioritize cutting the enemies off during retreat.

### Commander Hazards
As a game designed to be played in an immersive environment, we wish to differentiate from standard Real Time Strategy games where the player is looking at the battlefield from the sky in safety. Therefore, in the CAVE, players are standing on top of a command tower, where enemy arrows can easily reach them. While commanding their troops, player must also look out for flaming arrows aimed at them, and dodge them physically within the CAVE. If the player is shot too many times, or the player walks off the platform in an attempt to dodge, the player also loses, even if the cannons are safe. 

---

## Gamemodes

### Demo Mode
Demo Mode is recommended for beginners, as it is an easier and shorter version of the full mode. There are fewer waves of enemies, cannons get ready quicker, and troops (both enemy and friendly) move at a slower speed, giving the player more time to react and reposition. Demo Mode lasts for ~85 seconds.

### Full Mode
Full Mode is the intended experience of the game. Players must manage their troops well, regroup when necessary, and be ready to cut off retreating foes in order to win. Full Mode lasts for ~120 seconds.

### Dodge the Arrows
For players who enjoy the dodging aspect of the game, this is a minigame submode for you. Choose either the **Timed Challenge** or **Endless Survival** to experience the thrill of dodging for your life. Players get 3 hitpoints in the Timed Challenge to survive for 30 seconds, while players only get 1 hitpoint in the Endless Survival and must try to survive for as long as they can. As time goes on, the frequency of the arrows are increased, if you want to give yourself a challenge, try to survive for a minute in the Endless Survival!

### Siege PvP
In a siege, there is always an attacking commander and a defending commander. Although in the singleplayer mode you always play as the attacker, you can also play as the defender in the multiplayer mode Siege PvP. The defender would be given fewer troops than the attacker, and has to destroy the cannons within a time limit. 

---

## Setup

### Launching the Game

This game is intended for CAVE only, and is not tested for PC environment or HMD environment. In the CAVE machines, find in the root of the game a file named `Siege Command Simulator_[CAVE][XR].bat`, double click to launch. Make sure the controller is turned on when playing this game. Alternatively, you can use arrow keys and the Enter key to navigate the main menu, though the Demo, Full, and Siege PvP modes require a controller to be played properly, keyboard shortcuts are useful for quick testing and for launching dodge the arrows without needing a controller.

### Multiplayer
After booting up the game for the first time on both machines, you should find `network-config.json` (sometimes `network-configc.json` or `network-configh.json`) in the same folder as the `SiegeCommandSimulator.exe`. Open the file on both machines and configure to match one of the following, depending on whether the machine is the host or the client (under normal circumstances, the DASECave should always be the host, and the LEDCave should always be the client):
```
{
  "role": "host",
  "username": "DASECave",
  "host-ip": "192.168.0.242",
  "port": 7777
}
```
```
{
  "role": "client",
  "username": "LEDCave",
  "host-ip": "192.168.0.242",
  "port": 7777
}
```
Note that the IP above may not be correct. To find the correct IP address, open terminal on the host machine, then run `ipconfig` and find the local IP address.

After saving the updated file, launch the game first on the host machine, wait for it to finish loading, then launch the game on the client machine. Then, select Siege PvP on both machines, the multiplayer match should then begin.

---

## Credits
- Creator: Jim Tze Lau (Hogan) aka SuperNoobCraft
- Supervised by Ms. Bo Hui, HKU Visioneers
- Playtesters: Rafi, Joshua Chow, Josiah Chan
- Castle and Troop Models: Blackspire@Sketchfab
- Hyrule Fields Model: Ferere69@Sketchfab
- Sound Effects: pixabay.com, www.textavoice.com
- Included assets from "AllSkyFree" and "VFXPack_Fire" packages
