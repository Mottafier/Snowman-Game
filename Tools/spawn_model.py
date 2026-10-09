"""Ask for a word or phrase, have Claude design a low poly model of it, and send it to the running game.

The game side is Assets/Objects/Managers/TerminalModelSpawner.cs, which listens on 127.0.0.1:5055.
Needs the `anthropic` package (pip install -r Tools/requirements.txt) and an ANTHROPIC_API_KEY.
"""
import json
import socket
import sys
import time

import anthropic

PORT = 5055
MODEL = "claude-opus-5-5"

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
"""

VECTOR = {
    "type": "object",
    "properties": {"x": {"type": "number"}, "y": {"type": "number"}, "z": {"type": "number"}},
    "required": ["x", "y", "z"],
    "additionalProperties": False,
}

MODEL_SCHEMA = {
    "type": "object",
    "properties": {
        "name": {"type": "string"},
        "parts": {
            "type": "array",
            "items": {
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
            },
        },
    },
    "required": ["name", "parts"],
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


def design_model(client, prompt):
    """Returns the model spec dict, or None if Claude couldn't make one."""
    response = client.beta.messages.create(
        model=MODEL,
        max_tokens=16000,
        betas=["server-side-fallback-2026-07-01"],
        fallbacks="default",
        output_config={
            "effort": "medium",
            "format": {"type": "json_schema", "schema": MODEL_SCHEMA},
        },
        system=SYSTEM_PROMPT,
        messages=[{"role": "user", "content": f"Design a low poly model of: {prompt}"}],
    )

    if response.stop_reason == "refusal":
        print("  Claude declined to make that one. Try another word.")
        return None
    if response.stop_reason == "max_tokens":
        print("  The design got too long and was cut off. Try again or pick something simpler.")
        return None

    text = next((block.text for block in response.content if block.type == "text"), None)
    if text is None:
        print("  Claude didn't return a model. Try again.")
        return None
    return json.loads(text)


def main():
    try:
        client = anthropic.Anthropic()
    except anthropic.AnthropicError as e:
        print(f"Couldn't set up the Claude client: {e}")
        print("Set your API key first, e.g.:  setx ANTHROPIC_API_KEY \"sk-ant-...\"  (then open a new terminal)")
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
        try:
            spec = design_model(client, prompt)
        except anthropic.AuthenticationError:
            print("  Your API key was rejected. Check ANTHROPIC_API_KEY.\n")
            continue
        except anthropic.RateLimitError:
            print("  Hit the API rate limit. Wait a moment and try again.\n")
            continue
        except anthropic.APIStatusError as e:
            print(f"  The API returned an error ({e.status_code}): {e.message}\n")
            continue
        except anthropic.APIConnectionError:
            print("  Couldn't connect to the Claude API. Check your internet connection.\n")
            continue

        if spec is None:
            print()
            continue

        try:
            send_to_game(spec)
        except OSError:
            print("  The game stopped before the model could be sent.\n")
            continue

        print(f"  Spawned {spec['name']} ({len(spec['parts'])} parts, {time.time() - started:.0f}s)\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())
