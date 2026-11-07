#!/usr/bin/env python3
"""Compile-checks AFTERFALL's Unity assemblies without a licensed Unity Editor.

It compiles the project's asmdef assemblies (and the package assemblies they depend
on) with the .NET SDK against Unity's own reference assemblies, using Unity-like
scripting defines, in two passes:

  player : runtime assemblies, no UNITY_EDITOR (what a standalone build compiles)
  editor : runtime + editor assemblies with UNITY_EDITOR

This catches API misuse and type errors. It does NOT replace opening the project in
Unity: it cannot run code, import assets, or validate scenes/serialization.

Requirements
  - .NET SDK (dotnet) on PATH
  - Unity 6000.3.x editor contents: an installed editor
      (e.g. /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents) or the
      Contents folder extracted from the official installer .pkg
  - Package sources for registry packages (inputsystem, cinemachine, splines,
    mathematics, ai.navigation, nuget.newtonsoft-json): either the project's
    Library/PackageCache (after Unity opened the project once) or a folder of
    extracted package tarballs (<dir>/<package-name>/package/...).

Usage
  python3 tools/unity-compile-check/compile_check.py --unity <Contents> [--packages <dir>] [--pass player|editor|both]
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BUILD = os.path.join(ROOT, "tools", "unity-compile-check", ".build")
UNITY_VERSION = (6000, 3, 28)

# Registry packages compiled from source, in dependency order: (package, asmdef relative path)
PACKAGE_ASSEMBLIES = [
    ("com.unity.mathematics", "Unity.Mathematics/Unity.Mathematics.asmdef"),
    ("com.unity.splines", "Runtime/Unity.Splines.asmdef"),
    ("com.unity.inputsystem", "InputSystem/Unity.InputSystem.asmdef"),
    ("com.unity.cinemachine", "Runtime/Unity.Cinemachine.asmdef"),
    ("com.unity.ai.navigation", "Runtime/Unity.AI.Navigation.asmdef"),
]
# Test framework assemblies shipped inside the editor (BuiltInPackages): (package, asmdef relative path)
BUILTIN_TEST_ASSEMBLIES = [
    ("com.unity.test-framework", "UnityEngine.TestRunner/UnityEngine.TestRunner.asmdef"),
    ("com.unity.test-framework", "UnityEditor.TestRunner/UnityEditor.TestRunner.asmdef"),
]
# Packages considered "installed" for versionDefines evaluation.
INSTALLED = {
    "com.unity.mathematics", "com.unity.splines", "com.unity.inputsystem", "com.unity.cinemachine",
    "com.unity.ai.navigation", "com.unity.nuget.newtonsoft-json", "com.unity.test-framework",
    "com.unity.modules.physics", "com.unity.modules.animation", "com.unity.modules.uielements",
    "com.unity.modules.terrain", "com.unity.modules.ai", "com.unity.modules.audio",
    "com.unity.modules.imgui", "com.unity.modules.particlesystem", "com.unity.modules.ui",
}
# Optional integrations deliberately left out of the check (their assemblies are not compiled here).
NOT_COMPILED = {"com.unity.ugui", "com.unity.render-pipelines.universal", "com.unity.timeline"}

NOWARN = "CS0618;CS0649;CS0169;CS0414;CS1591;CS0067;CS0168;CS0219;CS0162;CS8632;CS0108;CS0114;CS0109;CS0659;CS0661;CS0660;CS1998;CS0436;CS0420;CS1717;CS0612;CS0672;CS0809;CS0628;CS0693;CS8321;CS0105;CS1570;CS1572;CS1573;CS1587;CS1574;CS1584;CS1658;CS1580;CS0419;CS1711;CS1734;CS3021;CS0675;CS0652"


def base_defines(editor):
    major, minor, patch = UNITY_VERSION
    d = [f"UNITY_{major}", f"UNITY_{major}_{minor}", f"UNITY_{major}_{minor}_{patch}"]
    for m in range(0, minor + 1):
        d.append(f"UNITY_{major}_{m}_OR_NEWER")
    for y in range(2017, 2024):
        for m in range(1, 5):
            d.append(f"UNITY_{y}_{m}_OR_NEWER")
    d += [f"UNITY_5_{m}_OR_NEWER" for m in range(3, 7)]
    d += [
        "PLATFORM_ARCH_64", "UNITY_64", "ENABLE_AUDIO", "ENABLE_CLOTH", "ENABLE_MICROPHONE", "ENABLE_MULTIPLE_DISPLAYS",
        "ENABLE_PHYSICS", "ENABLE_TEXTURE_STREAMING", "ENABLE_LZMA", "ENABLE_UNITYEVENTS", "ENABLE_VR", "ENABLE_WEBCAM",
        "ENABLE_UNITYWEBREQUEST", "ENABLE_WWW", "ENABLE_CLOUD_SERVICES", "ENABLE_INPUT_SYSTEM", "ENABLE_MONO",
        "NET_STANDARD_2_0", "NET_STANDARD_2_1", "NET_STANDARD", "NETSTANDARD2_1", "NETSTANDARD",
        "CSHARP_7_3_OR_NEWER", "CSHARP_7_OR_LATER", "UNITY_STANDALONE_OSX", "UNITY_STANDALONE", "PLATFORM_STANDALONE_OSX",
        "PLATFORM_STANDALONE", "ENABLE_GAMECENTER", "ENABLE_SPATIALTRACKING", "ENABLE_MANAGED_JOBS",
        "ENABLE_MANAGED_TRANSFORM_JOBS", "ENABLE_MANAGED_ANIMATION_JOBS", "ENABLE_MANAGED_AUDIO_JOBS",
        "ENABLE_RUNTIME_PERMISSIONS", "UNITY_INCLUDE_TESTS", "UNITY_TESTS_FRAMEWORK",
    ]
    if editor:
        d += ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_OSX", "UNITY_ASSERTIONS", "ENABLE_UNITY_COLLECTIONS_CHECKS", "ENABLE_BURST_AOT"]
    return d


def parse_version(v):
    nums = re.findall(r"\d+", v)
    return tuple(int(n) for n in nums[:3]) + (0,) * (3 - len(nums[:3]))


def expr_satisfied(expr, version):
    expr = expr.strip()
    if not expr:
        return True
    m = re.match(r"^([\[\(])\s*([^,]*)\s*,\s*([^\]\)]*)\s*([\]\)])$", expr)
    if m:
        lo_inc, lo, hi, hi_inc = m.group(1) == "[", m.group(2), m.group(3), m.group(4) == "]"
        if lo:
            lv = parse_version(lo)
            if version < lv or (version == lv and not lo_inc):
                return False
        if hi:
            hv = parse_version(hi)
            if version > hv or (version == hv and not hi_inc):
                return False
        return True
    if expr.startswith("[") and expr.endswith("]"):
        return version == parse_version(expr[1:-1])
    return version >= parse_version(expr)


def version_defines(asmdef):
    out = []
    for vd in asmdef.get("versionDefines", []):
        name, define, expr = vd.get("name"), vd.get("define"), vd.get("expression", "")
        if name == "Unity":
            if expr_satisfied(expr, UNITY_VERSION):
                out.append(define)
        elif name in INSTALLED and name not in NOT_COMPILED:
            out.append(define)
    return out


def find_package(pkg, pkg_dirs):
    for d in pkg_dirs:
        for cand in [os.path.join(d, pkg, "package"), os.path.join(d, pkg)] + glob.glob(os.path.join(d, pkg + "@*")):
            if os.path.isfile(os.path.join(cand, "package.json")):
                return cand
    return None


def sources_for(asmdef_path):
    """All .cs under the asmdef folder, excluding subfolders that own another asmdef/asmref."""
    root = os.path.dirname(asmdef_path)
    files = []
    for dirpath, dirnames, filenames in os.walk(root):
        if dirpath != root and (glob.glob(os.path.join(dirpath, "*.asmdef")) or glob.glob(os.path.join(dirpath, "*.asmref"))):
            dirnames[:] = []
            continue
        dirnames[:] = [d for d in dirnames if not d.endswith("~") and not d.startswith(".")]
        files += [os.path.join(dirpath, f) for f in filenames if f.endswith(".cs")]
    return files


def unity_refs(contents, editor):
    scripting = os.path.join(contents, "Resources", "Scripting")
    managed = os.path.join(scripting, "Managed")
    ns = os.path.join(scripting, "NetStandard")
    refs = [os.path.join(ns, "ref", "2.1.0", "netstandard.dll")]
    refs += sorted(glob.glob(os.path.join(ns, "compat", "2.1.0", "shims", "netfx", "*.dll")))
    for f in sorted(glob.glob(os.path.join(managed, "UnityEngine", "*.dll"))):
        name = os.path.basename(f)
        if name.startswith("UnityEditor") and not editor:
            continue
        if name == "UnityEngine.dll":
            continue
        refs.append(f)
    if editor:
        refs.append(os.path.join(managed, "UnityEditor.Graphs.dll"))
    return refs


def write_csproj(path, name, files, refs, project_refs, defines, unsafe, outdir):
    lines = [
        '<Project Sdk="Microsoft.NET.Sdk">',
        "  <PropertyGroup>",
        "    <TargetFramework>netstandard2.1</TargetFramework>",
        "    <LangVersion>9.0</LangVersion>",
        f"    <AssemblyName>{name}</AssemblyName>",
        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
        "    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>",
        "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>",
        "    <Nullable>disable</Nullable>",
        f"    <AllowUnsafeBlocks>{'true' if unsafe else 'false'}</AllowUnsafeBlocks>",
        f"    <DefineConstants>{';'.join(defines)}</DefineConstants>",
        f"    <NoWarn>{NOWARN}</NoWarn>",
        f"    <OutputPath>{outdir}</OutputPath>",
        "    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>",
        "    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
    ]
    for f in files:
        lines.append(f'    <Compile Include="{f}" />')
    for r in refs:
        rname = os.path.splitext(os.path.basename(r))[0]
        lines.append(f'    <Reference Include="{rname}"><HintPath>{r}</HintPath><Private>false</Private></Reference>')
    for p in project_refs:
        lines.append(f'    <ProjectReference Include="{p}" />')
    lines += ["  </ItemGroup>", "</Project>", ""]
    with open(path, "w") as fh:
        fh.write("\n".join(lines))


def project_asmdefs():
    found = {}
    for path in glob.glob(os.path.join(ROOT, "Assets", "**", "*.asmdef"), recursive=True):
        with open(path) as fh:
            data = json.load(fh)
        found[data["name"]] = (path, data)
    return found


def run_pass(args, editor):
    tag = "editor" if editor else "player"
    outdir = os.path.join(BUILD, tag, "bin")
    projdir = os.path.join(BUILD, tag, "proj")
    if os.path.isdir(os.path.join(BUILD, tag)):
        shutil.rmtree(os.path.join(BUILD, tag))
    os.makedirs(projdir)
    defines = base_defines(editor)
    refs = unity_refs(args.unity, editor)
    pkg_dirs = [d for d in [args.packages, os.path.join(ROOT, "Library", "PackageCache")] if d and os.path.isdir(d)]

    csprojs = {}
    known = {}

    for pkg, rel in PACKAGE_ASSEMBLIES:
        pdir = find_package(pkg, pkg_dirs)
        if pdir is None:
            print(f"error: package source for {pkg} not found in {pkg_dirs}", file=sys.stderr)
            return False
        asm_path = os.path.join(pdir, rel)
        with open(asm_path) as fh:
            data = json.load(fh)
        known[data["name"]] = (asm_path, data)
        # Package GUID references (e.g. Splines -> Mathematics) are resolved by name below.

    builtin = os.path.join(args.unity, "Resources", "PackageManager", "BuiltInPackages")
    nunit_dll = os.path.join(builtin, "com.unity.ext.nunit", "net40", "unity-custom", "nunit.framework.dll")
    if editor:
        for pkg, rel in BUILTIN_TEST_ASSEMBLIES:
            asm_path = os.path.join(builtin, pkg, rel)
            if os.path.isfile(asm_path):
                with open(asm_path) as fh:
                    data = json.load(fh)
                known[data["name"]] = (asm_path, data)
        tf = find_package("com.unity.inputsystem", pkg_dirs)
        if tf:
            asm_path = os.path.join(tf, "Tests", "TestFixture", "Unity.InputSystem.TestFramework.asmdef")
            if os.path.isfile(asm_path):
                with open(asm_path) as fh:
                    known["Unity.InputSystem.TestFramework"] = (asm_path, json.load(fh))

    newtonsoft = find_package("com.unity.nuget.newtonsoft-json", pkg_dirs)
    newtonsoft_dll = os.path.join(newtonsoft, "Runtime", "Newtonsoft.Json.dll") if newtonsoft else None

    proj = project_asmdefs()
    known.update(proj)
    guid_to_name = {
        "d8b63aba1907145bea998dd612889d6b": "Unity.Mathematics",
        "2665a8d13d1b3f18800f46e256720795": "Unity.Burst",
    }

    def include(name, data):
        plats = data.get("includePlatforms", [])
        if plats and "Editor" in plats and not editor:
            return False
        if data.get("defineConstraints"):
            for c in data["defineConstraints"]:
                neg = c.startswith("!")
                sym = c[1:] if neg else c
                has = sym in defines
                if has == neg:
                    return False
        return True

    order = []

    def visit(name, stack=()):
        if name in csprojs or name in order:
            return True
        if name not in known:
            return False
        path, data = known[name]
        if not include(name, data):
            return False
        for r in data.get("references", []):
            rn = guid_to_name.get(r[5:], r) if r.startswith("GUID:") else r
            if rn not in stack:
                visit(rn, stack + (name,))
        order.append(name)
        return True

    targets = [n for n, (p, d) in proj.items() if editor or not n.startswith("Afterfall.Tests")]
    for t in targets:
        visit(t)

    for name in order:
        path, data = known[name]
        files = sources_for(path)
        prefs = []
        for r in data.get("references", []):
            rn = guid_to_name.get(r[5:], r) if r.startswith("GUID:") else r
            if rn in csprojs:
                prefs.append(csprojs[rn])
        extra = list(refs)
        if newtonsoft_dll and (name.startswith("Afterfall") or "Newtonsoft.Json.dll" in data.get("precompiledReferences", [])):
            extra.append(newtonsoft_dll)
        if "nunit.framework.dll" in data.get("precompiledReferences", []) and os.path.isfile(nunit_dll):
            extra.append(nunit_dll)
        d = defines + version_defines(data)
        cs = os.path.join(projdir, name, name + ".csproj")
        os.makedirs(os.path.dirname(cs), exist_ok=True)
        write_csproj(cs, name, files, extra, prefs, d, data.get("allowUnsafeCode", False), outdir)
        csprojs[name] = cs

    ok = True
    roots = [csprojs[n] for n in order if n.startswith("Afterfall")]
    for cs in roots:
        print(f"[{tag}] building {os.path.basename(cs)}")
        r = subprocess.run(["dotnet", "build", cs, "-nologo", "-v", "q", "-clp:NoSummary"], capture_output=True, text=True)
        errors = sorted(set(l for l in (r.stdout + r.stderr).splitlines() if ": error " in l))
        if r.returncode != 0 or errors:
            ok = False
            for e in errors[:200]:
                print("  " + e.replace(ROOT + "/", ""))
            if not errors:
                print(r.stdout[-4000:])
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--unity", required=True, help="Unity.app/Contents of a 6000.3 editor")
    ap.add_argument("--packages", help="folder with extracted registry package sources")
    ap.add_argument("--pass", dest="which", default="both", choices=["player", "editor", "both"])
    args = ap.parse_args()
    passes = [False, True] if args.which == "both" else [args.which == "editor"]
    results = [run_pass(args, e) for e in passes]
    print("COMPILE CHECK: " + ("PASSED" if all(results) else "FAILED"))
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
