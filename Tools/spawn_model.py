"""Ask for a word or phrase, have Claude design a low poly model of it, and send it to the running game.

The game side is Assets/Objects/Managers/TerminalModelSpawner.cs, which listens on 127.0.0.1:5055.
Uses the Claude Code CLI (`claude`) signed in to your Claude plan, so there is no separate API bill.
"""
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time

PORT = 5055
CLAUDE_TIMEOUT_SECONDS = 300

SYSTEM_PROMPT = """\
You design low poly 3D models for a cozy snowy game. Every model is built only from simple shapes.

Shapes (each fits a 1x1x1 box centered on its position before scaling):
- sphere: low poly ellipsoid
- cube: box
- cylinder: axis along local Y
- cone: base at local y=-0.5, tip at local y=+0.5

Coordinates are Unity's, in meters: Y is up, the model's front faces +Z, and +X is the model's right.
Stand the model on y=0 and center it on x=0, z=0.
- position: center of the shape
- scale: size along the shape's local X, Y and Z in meters
- rotation: Euler angles in degrees, applied Z first, then X, then Y.
  For example rotation x=90 points a cone's tip (+Y) toward +Z, and z=90 points it toward -X.

Guidelines:
- Use realistic real-world sizes (a mug is about 0.1 m, a person 1.8 m, a car 4.5 m). The game rescales very large or tiny objects.
- Capture the most recognizable silhouette and features, slightly exaggerated like a caricature so it reads at a glance.
- Overlap shapes so there are no gaps or floating pieces, unless the object really has them.
- Use between 5 and 60 parts. Flat, saturated colors with r, g, b from 0 to 1.
- People and characters: make them recognizable through build, clothing, hair, colors and signature accessories.
- Abstract words or actions: build the most iconic object or symbol associated with them.
- name: a short display name for the model.
- count: how many copies of the model to spawn, read from the phrase. A single thing ("goblin", "a banana") is 1.
  Groups scale with the wording: "a few" or "some" about 3, "a pack" or "a gang" 4 to 8, "a horde" or "a swarm"
  8 to 15, "an army" 20 to 50. Never more than 50. Plain plurals ("goblins") are 2 to 4.
  When count is above 10, keep the model simple (15 parts or fewer) and make each one weaker (lower health and damage)
  so the group stays fair. Respect size words like "tiny" or "giant".

Every model comes alive as an enemy that chases and attacks the player, who fights back with snowballs
(25 damage each; the player has 100 HP). Pick a behavior that suits what the thing is:
- movement: walk (steady), hop (bouncing jumps), fly (hovers above the ground), charge (winds up, then dashes),
  zigzag (weaves side to side), teleport (blinks to a new spot near the player every few seconds),
  burrow (zips around underground very fast for 5-10 seconds where it can't be hit, then pops up at attack
  distance and attacks for about 3 seconds before diving again; its speed only matters above ground),
  orbit (circles the player at attack distance)
- speed: meters per second, 0.3 (crawling) to 2 (very fast). The player is slow (1 to 3.5 depending on how much
  snow they carry), so most enemies should be 0.5 to 1.2 and only a rare few above 1.5
- attack: melee (lunges and hits up close), ranged (one shot from a distance), burst (three quick shots in a row),
  spread (five shots fanned out like a shotgun), homing (one slow shot that curves after the player),
  slam (jumps and crashes down, hitting everything within range), explode (runs up and blows up, once)
- damage per hit, 1 to 50; attackRange in meters (melee 0.8-3; ranged, burst, spread and homing 3-15; slam 1.5-4;
  explode 1-4); attackCooldown in seconds between attacks (0.4-6); health 25 to 400.
  Burst and spread fire several shots, so give them lower damage per shot.
- special: none, splits (breaks into two smaller, weaker copies when killed), heals (every few seconds heals other
  hurt enemies nearby), slows (its hits slow the player for 2 seconds), enrages (below half health it gets faster
  and attacks more often). Most enemies should have none; use one when it really fits the thing (a slime splits,
  a medic heals, a snowman or ice monster slows, a bull or angry bear enrages).
Keep it fair and fun: fast or hard-hitting enemies should be fragile, and tough ones slow. Use the variety:
pick the movement and attack that best match how the thing would really move and fight.

projectile: what a ranged, burst, spread or homing enemy throws or shoots, designed with the same shapes and coordinates but centered
on the origin, with its front (the end that leads in flight) facing +Z. Keep it simple (1 to 12 parts) and make it
fit the enemy: a deck of cards throws a playing card, a cactus fires a spine, a pirate ship fires a cannonball.
The game resizes it, so use the real proportions. spin: none, spin (flat like a frisbee or thrown card),
roll (around its flight direction like a bullet or football) or tumble (end over end like a thrown axe).
For melee, slam and explode enemies, give an empty parts list and spin none.
"""

VECTOR = {
    "type": "object",
    "properties": {"x": {"type": "number"}, "y": {"type": "number"}, "z": {"type": "number"}},
    "required": ["x", "y", "z"],
    "additionalProperties": False,
}

PART = {
    "type": "object",
    "properties": {
        "shape": {"type": "string", "enum": ["sphere", "cube", "cylinder", "cone"]},
        "color": {
            "type": "object",
            "properties": {"r": {"type": "number"}, "g": {"type": "number"}, "b": {"type": "number"}},
            "required": ["r", "g", "b"],
            "additionalProperties": False,
        },
        "position": VECTOR,
        "rotation": VECTOR,
        "scale": VECTOR,
    },
    "required": ["shape", "color", "position", "rotation", "scale"],
    "additionalProperties": False,
}

MODEL_SCHEMA = {
    "type": "object",
    "properties": {
        "name": {"type": "string"},
        "count": {"type": "integer"},
        "parts": {"type": "array", "items": PART},
        "behavior": {
            "type": "object",
            "properties": {
                "movement": {"type": "string", "enum": ["walk", "hop", "fly", "charge", "zigzag", "teleport", "burrow", "orbit"]},
                "speed": {"type": "number"},
                "attack": {"type": "string", "enum": ["melee", "ranged", "burst", "spread", "homing", "slam", "explode"]},
                "damage": {"type": "integer"},
                "attackRange": {"type": "number"},
                "attackCooldown": {"type": "number"},
                "health": {"type": "integer"},
                "special": {"type": "string", "enum": ["none", "splits", "heals", "slows", "enrages"]},
            },
            "required": ["movement", "speed", "attack", "damage", "attackRange", "attackCooldown", "health", "special"],
            "additionalProperties": False,
        },
        "projectile": {
            "type": "object",
            "properties": {
                "parts": {"type": "array", "items": PART},
                "spin": {"type": "string", "enum": ["none", "spin", "roll", "tumble"]},
            },
            "required": ["parts", "spin"],
            "additionalProperties": False,
        },
    },
    "required": ["name", "count", "parts", "behavior", "projectile"],
    "additionalProperties": False,
}


def game_is_running():
    try:
        with socket.create_connection(("127.0.0.1", PORT), timeout=2):
            return True
    except OSError:
        return False


def send_to_game(spec):
    with socket.create_connection(("127.0.0.1", PORT), timeout=5) as conn:
        conn.sendall((json.dumps(spec) + "\n").encode("utf-8"))


def design_model(claude, prompt):
    """Returns the model spec dict, or None if Claude couldn't make one."""
    # Drop API credentials so the CLI uses the Claude plan sign-in rather than per-use API billing
    env = os.environ.copy()
    env.pop("ANTHROPIC_API_KEY", None)
    env.pop("ANTHROPIC_AUTH_TOKEN", None)

    command = [
        claude, "-p", f"Design a low poly model of: {prompt}",
        "--system-prompt", SYSTEM_PROMPT,
        "--json-schema", json.dumps(MODEL_SCHEMA),
        "--output-format", "json",
        "--model", "haiku",  # fastest model, so spawns come back quickly
        "--effort", "medium",
        "--tools", "",
        "--strict-mcp-config",
        "--no-session-persistence",
    ]
    try:
        # Run outside the project so no project settings or instructions get picked up
        proc = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", env=env,
                              cwd=tempfile.gettempdir(), timeout=CLAUDE_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        print("  Claude took too long. Try again.")
        return None

    try:
        result = json.loads(proc.stdout)
    except json.JSONDecodeError:
        print(f"  Couldn't read Claude's reply: {(proc.stderr or proc.stdout).strip()[:300]}")
        return None

    if result.get("is_error"):
        message = str(result.get("result", "unknown error"))
        if "login" in message.lower():
            print("  The claude command isn't signed in. Run `claude`, type /login, sign in with your Claude account, then try again.")
        else:
            print(f"  Claude returned an error: {message}")
        return None

    spec = result.get("structured_output")
    if spec is None:
        try:
            spec = json.loads(result.get("result", ""))
        except json.JSONDecodeError:
            print("  Claude didn't return a model. Try again.")
            return None
    return spec


def main():
    claude = shutil.which("claude")
    if claude is None:
        print("Couldn't find the `claude` command. Install Claude Code: https://claude.com/claude-code")
        return 1

    print("Type a word or phrase and it will appear in front of you in the game.")
    print("Press Enter on an empty line (or Ctrl+C) to quit.\n")

    while True:
        try:
            prompt = input("Input word or phrase: ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            break
        if not prompt:
            break

        if not game_is_running():
            print(f"  Couldn't reach the game on port {PORT}. Is it running in Play mode?\n")
            continue

        print(f"  Designing a low poly {prompt}...")
        started = time.time()
        spec = design_model(claude, prompt)
        if spec is None:
            print()
            continue

        try:
            send_to_game(spec)
        except OSError:
            print("  The game stopped before the model could be sent.\n")
            continue

        b = spec["behavior"]
        count = spec.get("count", 1)
        what = spec["name"] if count == 1 else f"{count} x {spec['name']}"
        print(f"  Spawned {what} ({len(spec['parts'])} parts each, {time.time() - started:.0f}s)")
        moves = {"fly": "flies"}.get(b["movement"], b["movement"] + "s")
        print(f"  {'It' if count == 1 else 'Each'} {moves} at {b['speed']:g} m/s, {b['attack']} attack for {b['damage']} damage, {b['health']} HP"
              + ("" if b.get("special", "none") == "none" else f", special: {b['special']}") + "\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())
