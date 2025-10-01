# Artifacts MMO Client

The goal of this program is to autonomously play the Artifacts MMO, which is a web-based RPG that is meant to be played through it's API by "bots".

This project will contain a web-API, allowing a player to give higher level commands to their characters, than the official Artifacts MMO allows, e.g allowing a player to command their character to just "obtain a resource", with the program figuring out how itself, and eventually the program should be able to play the game itself, with little guidance.

## Setup

Rename "appSettings.Local.template.json" -> "appSettings.Local.json" and fill in the "AccountName" and "ApiToken" environment variables.

## Roadmap

- Allow the player to command their characters, through a web API. They can queue jobs, override jobs, etc.
- Develop more sophisticated jobs, which can spawn other jobs, e.g a job for obtaining a crafted item, should be able to make all lower level jobs required to gather the ingredients, craft required components etc, and then finally craft the requested item.
- Functionality for the bots to break down tasks (in-game missions/quests), and complete them in their entirety without guidance
- Use the same functionality as above for "higher level tasks" - maybe that's completing achievements, or player-made goals such as "maximize XP" or "upgrade your equipment sufficiently"

Fun things, that would be nice to make as well:

- Characters should be able to be coordinated to complete jobs together, so e.g if character A needs 20 salmon, and character B has 20 salmon in their inventory, jobs should be made that coordinate that B goes to the bank, deposits their 20 salmon, and character A goes to the bank and withdraws them.
