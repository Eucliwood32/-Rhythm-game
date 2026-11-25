"""Basic Unity project checks that do not require the Unity Editor."""

from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
errors = []


def content(path: str) -> str:
    file = ROOT / path
    if not file.is_file():
        errors.append(f"Missing file: {path}")
        return ""
    return file.read_text(encoding="utf-8-sig")


version = content("ProjectSettings/ProjectVersion.txt")
if version and not re.search(r"(?m)^m_EditorVersion: 6000\.5\.1f1\s*$", version):
    errors.append("Unity version must be 6000.5.1f1")

scene = content("Assets/Scenes/SampleScene.unity")
content("Assets/Scenes/SampleScene.unity.meta")
if scene and not re.search(r"(?m)^\s*m_Name: Main Camera\s*$", scene):
    errors.append("SampleScene must contain Main Camera")

build_settings = content("ProjectSettings/EditorBuildSettings.asset")
if build_settings and not re.search(
    r"(?m)^\s*path: Assets/Scenes/SampleScene\.unity\s*$", build_settings
):
    errors.append("SampleScene must remain in the build scene list")

editor_settings = content("ProjectSettings/EditorSettings.asset")
if editor_settings and not re.search(
    r"(?m)^\s*m_SerializationMode: 2\s*$", editor_settings
):
    errors.append("Set Asset Serialization to Force Text")

vc_settings = content("ProjectSettings/VersionControlSettings.asset")
if vc_settings and "m_Mode: Visible Meta Files" not in vc_settings:
    errors.append("Set Version Control Mode to Visible Meta Files")

assets = ROOT / "Assets"
if not assets.is_dir():
    errors.append("Missing Assets directory")
else:
    for file in assets.rglob("*"):
        if file.is_file() and file.suffix != ".meta":
            if not file.with_name(file.name + ".meta").is_file():
                errors.append(f"Missing .meta: {file.relative_to(ROOT)}")

    template_scripts = {
        "Settings/GlobalVolumeSettings.cs",
        "TutorialInfo/Scripts/Readme.cs",
        "TutorialInfo/Scripts/Editor/ReadmeEditor.cs",
    }
    for file in assets.rglob("*.cs"):
        rel = file.relative_to(assets).as_posix()
        if rel not in template_scripts and rel.split("/")[0] not in {
            "MyScripts",
            "OtherScripts",
        }:
            errors.append(f"Script outside MyScripts/OtherScripts: Assets/{rel}")

tracked = subprocess.run(
    ["git", "ls-files", "-z"],
    cwd=ROOT,
    check=True,
    capture_output=True,
).stdout.decode("utf-8").split("\0")
generated = {"Library", "Temp", "Obj", "Build", "Builds", "Logs", "UserSettings"}
for path in tracked:
    if path and path.split("/")[0] in generated:
        errors.append(f"Generated directory tracked by Git: {path}")

if errors:
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    sys.exit(1)

print("Project checks passed (static checks only; Unity was not run).")
