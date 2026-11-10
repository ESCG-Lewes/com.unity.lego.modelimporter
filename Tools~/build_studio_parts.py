#!/usr/bin/env python3
"""Build supplemental FBX archives from a local BrickLink Studio installation."""

import argparse
import csv
import functools
import hashlib
import math
from pathlib import Path
import re
import tempfile
import uuid
import xml.etree.ElementTree as ET
import zipfile

PART_NAME = re.compile(r"[a-z0-9_.+-]+\.dat", re.ASCII)
LIMIT = 100_000_000
# Animal, Food & Drink, Plant, and props under Minifigure, Object and Friends.
COMMON_SHAPES = {"Brick", "Modified Brick", "Round Brick", "Plate", "Modiﬁed Plate",
                 "Round / Cut Plate", "Slope", "Curved Brick / Slope", "Door / Window", "Building"}
BUILDING_FAMILIES = ("Brick", "Slope", "Plate", "Tile", "Cylinder", "Dish", "Bar", "Cone", "Wedge")
SMALL_FOOTPRINT = re.compile(r"\b[1-3]\s*x\s*[1-3]\b(?!\s*/)", re.IGNORECASE)
MOULD_VARIANT = re.compile(r"[0-9]+[a-z]\.dat", re.ASCII)
OBJECT_SUBCATEGORIES = {"Minifigure, Utensil", "Minifigure, Weapon", "Minifigure, Shield"}
MINIFIG_SUBCATEGORIES = {
    "Minifigure, Body Part", "Minifigure, Body Wear", "Minifigure, Hair",
    "Minifigure, Head", "Minifigure, Head, Modified", "Minifigure, Headgear",
    "Minifigure, Headgear Accessory", "Minifigure, Legs", "Minifigure, Legs, Decorated",
    "Minifigure, Legs, Modified", "Minifigure, Legs, Modified, Decorated",
    "Minifigure, Torso", "Minifigure, Torso Assembly", "Minifigure, Torso Assembly, Decor."
}


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        yield from csv.DictReader(source, delimiter="\t")


def catalogue(studio):
    data = studio / "data"
    categories = {
        (row["BL Index"], row["subIndex"]): (row["BL CatalogName"].strip(), row["Shape Name"].strip())
        for row in rows(data / "StudioCategoryDefinition.txt")
        if row["Shape Name"].strip() == "Object"
        or row["BL CatalogName"].strip().startswith(tuple(OBJECT_SUBCATEGORIES))
        or row["BL CatalogName"].strip() in MINIFIG_SUBCATEGORIES
        or row["BL Index"] in {"3", "6", "41", "42", "68"}
        or row["Shape Name"].strip() in COMMON_SHAPES
    }
    for row in rows(data / "StudioPartDefinition2.txt"):
        category = categories.get((row["BLCatalogIndex"], row["BLCatalogSubIndex"]))
        name = row["LDraw ItemNo"].strip().lower()
        if name and not name.endswith(".dat"):
            name = "bl_" + name + ".dat"
        if category and PART_NAME.fullmatch(name) and not row["flexible type"].strip():
            label, shape = category
            description = row["Description"]
            building_shape = shape in COMMON_SHAPES or label == "Bar"
            if label.startswith("Technic") or (building_shape and row["IsDecorated"].strip().casefold() != "false"):
                continue
            # Printed variants reuse the same mould and need decoration assets
            # that are not part of the FBX geometry set. Keep the unprinted
            # minifigure legs, torsos, heads, hair and hats here.
            if row["BLCatalogIndex"] == "59" and row["IsDecorated"].strip().casefold() != "false":
                continue
            if label == "Bar" and re.search(r"\bTechnic\b", description, re.IGNORECASE):
                continue
            if row["BLCatalogIndex"] == "59":
                group = "Minifigs"
            elif row["BLCatalogIndex"] == "3":
                group = "Animals"
            else:
                group = "Shapes" if building_shape else "Props"
            yield name, group, label, description


def shipped_parts(data):
    shipped = set()
    for name in ("NewParts", "LegacyParts"):
        archive = data / (name + ".zip")
        if archive.is_file():
            with zipfile.ZipFile(archive) as parts:
                entries = parts.namelist()
        else:
            chunks = sorted(data.glob(name + ".zip.[0-9][0-9][0-9]"))
            if not chunks:
                raise FileNotFoundError(f"No {name} archive under {data}")
            with tempfile.TemporaryFile() as combined:
                for chunk in chunks:
                    with chunk.open("rb") as source:
                        while block := source.read(1024 * 1024):
                            combined.write(block)
                with zipfile.ZipFile(combined) as parts:
                    entries = parts.namelist()
        for entry in entries:
            if entry.endswith(".fbx"):
                stem = Path(entry).stem
                shipped.add(stem[1:] if name == "NewParts" and stem.startswith("m") else stem)
    return shipped


class Library:
    def __init__(self, root):
        self.index = {}
        for folder in ("UnOfficial/parts", "parts", "LEGO", "UnOfficial/p", "p"):
            base = root / folder
            for path in sorted(base.rglob("*.dat")):
                self.index.setdefault(path.relative_to(base).as_posix().lower(), path)

    @functools.lru_cache(maxsize=4096)
    def commands(self, name):
        path = self.index.get(name)
        if path is None and "/" not in name:
            path = self.index.get("48/" + name) or self.index.get("8/" + name)
        if path is None and name.startswith(("48/", "8/")):
            # Studio sometimes stores these primitives directly in p even
            # when the referring file includes the resolution folder.
            primitive = self.index.get(name.partition("/")[2])
            if primitive is not None and primitive.parent.name == "p":
                path = primitive
        if path is None:
            raise ValueError(f"Missing dependency {name}")
        commands = []
        for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
            tokens = line.split(maxsplit=14)
            if not tokens:
                continue
            code = tokens[0]
            if code == "0" and tokens[1:2] == ["BFC"]:
                commands.append(("bfc", tokens[2:]))
            elif code == "1":
                values = tuple(map(float, tokens[2:14]))
                commands.append(("ref", (values, tokens[14].replace("\\", "/").strip().lower())))
            elif code in {"3", "4"}:
                values = tuple(map(float, line.split()[2:2 + int(code) * 3]))
                if len(values) != int(code) * 3:
                    raise ValueError(f"Malformed face in {name}")
                commands.append(("face", tuple(zip(values[::3], values[1::3], values[2::3]))))
        return commands

    def triangles(self, name, parents=(), inverted=False, clip=True):
        if name in parents or len(parents) >= 64:
            raise ValueError(f"Cyclic or excessively nested dependency {name}")
        clockwise = certified = invert_next = False
        local_clip = True
        for command, values in self.commands(name):
            if command == "bfc":
                if "CW" in values or "CCW" in values:
                    clockwise = "CW" in values
                if "CERTIFY" in values or "NOCERTIFY" in values:
                    certified = "CERTIFY" in values
                if "CLIP" in values or "NOCLIP" in values:
                    local_clip = "CLIP" in values
                invert_next = "INVERTNEXT" in values
                continue

            if command == "ref":
                matrix, child = values
                x, y, z, a, b, c, d, e, f, g, h, i = matrix
                mirrored = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g) < 0
                for triangle in self.triangles(child, parents + (name,), inverted ^ invert_next ^ mirrored,
                                               clip and local_clip):
                    yield tuple((x+a*u+b*v+c*w, y+d*u+e*v+f*w, z+g*u+h*v+i*w) for u, v, w in triangle)
            else:
                for j in range(1, len(values) - 1):
                    triangle = (values[0], values[j], values[j + 1])
                    if clockwise ^ inverted:
                        triangle = triangle[::-1]
                    yield triangle
                    if not (certified and clip and local_clip):
                        yield triangle[::-1]
            invert_next = False


def make_fbx(library, name):
    vertices = {}
    indices = []
    normals = []
    for triangle in library.triangles(name):
        # FBX is right handed, with +Y up and centimetres as its file unit.
        # Unity mirrors X on import. Preserve the LDraw model importer's origin and axes.
        points = [(round(x * 4, 6), round(-y * 4, 6), round(-z * 4, 6)) for x, y, z in triangle]
        a, b, c = points
        ux, uy, uz = (right - left for left, right in zip(a, b))
        vx, vy, vz = (right - left for left, right in zip(a, c))
        normal = (uy*vz-uz*vy, uz*vx-ux*vz, ux*vy-uy*vx)
        length = math.hypot(*normal)
        if not math.isfinite(length):
            raise ValueError("Non-finite geometry")
        if length == 0:
            continue
        for point in points:
            indices.append(vertices.setdefault(point, len(vertices)))
        indices[-1] = -1 - indices[-1]
        unit_normal = tuple(value / length for value in normal)
        normals.extend(unit_normal * 3)
        if len(indices) > 1_500_000:
            raise ValueError("Face limit exceeded")
    if not indices:
        raise ValueError("No usable faces")

    coords = [value for point in vertices for value in point]
    def array(values):
        return ",".join(format(value, ".8g") if isinstance(value, float) else str(value) for value in values)

    stem = Path(name).stem
    return f"""; FBX 7.4.0 project file
FBXHeaderExtension: {{
    FBXHeaderVersion: 1003
    FBXVersion: 7400
}}
GlobalSettings: {{
    Version: 1000
    Properties70: {{
        P: "UpAxis", "int", "Integer", "",1
        P: "UpAxisSign", "int", "Integer", "",1
        P: "FrontAxis", "int", "Integer", "",2
        P: "FrontAxisSign", "int", "Integer", "",1
        P: "CoordAxis", "int", "Integer", "",0
        P: "CoordAxisSign", "int", "Integer", "",1
        P: "UnitScaleFactor", "double", "Number", "",1
    }}
}}
Documents: {{
    Count: 1
    Document: 1000, "", "Scene" {{
        RootNode: 0
    }}
}}
References: {{
}}
Definitions: {{
    Version: 100
    Count: 2
    ObjectType: "Geometry" {{
        Count: 1
    }}
    ObjectType: "Model" {{
        Count: 1
    }}
}}
Objects: {{
    Geometry: 1001, "Geometry::{stem}", "Mesh" {{
        GeometryVersion: 124
        Vertices: *{len(coords)} {{
            a: {array(coords)}
        }}
        PolygonVertexIndex: *{len(indices)} {{
            a: {array(indices)}
        }}
        LayerElementNormal: 0 {{
            Version: 101
            Name: ""
            MappingInformationType: "ByPolygonVertex"
            ReferenceInformationType: "Direct"
            Normals: *{len(normals)} {{
                a: {array(normals)}
            }}
        }}
        Layer: 0 {{
            Version: 100
            LayerElement: {{
                Type: "LayerElementNormal"
                TypedIndex: 0
            }}
        }}
    }}
    Model: 1002, "Model::{stem}", "Mesh" {{
        Version: 232
        Shading: T
        Culling: "CullingOff"
    }}
}}
Connections: {{
    C: "OO",1001,1002
    C: "OO",1002,0
}}
""".encode("ascii")


def meta(path):
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "com.unity.lego.modelimporter/" + path.name).hex
    path.with_name(path.name + ".meta").write_text(f"fileFormatVersion: 2\nguid: {guid}\n")


def build(studio, data, output, only):
    shipped = shipped_parts(data)
    mapping = ET.parse(data / "ldraw.xml").getroot()
    known = {row.get("ldraw", "").lower() for row in mapping.findall("Brick") if row.get("lego") in shipped}
    assemblies = ET.parse(data / "ldrawassembly.xml").getroot()
    for assembly in assemblies.findall("Assembly"):
        components = [part.get("ldraw", "").lower() for part in assembly.findall("Part")]
        if components and all(name in known or Path(name).stem in shipped for name in components):
            known.add(assembly.get("ldraw", "").lower())

    selected = {}
    for name, group, category, description in catalogue(studio):
        if Path(name).stem in shipped:
            continue
        # A letter-suffixed LDraw mould can have distinct geometry even when
        # the Unity mapping resolves it to the same packaged design as another mould.
        exact_mould = (
            group == "Shapes"
            and category.startswith(BUILDING_FAMILIES)
            and MOULD_VARIANT.fullmatch(name)
            and (SMALL_FOOTPRINT.search(description) or category in {"Bar", "Cylinder", "Dish"})
        )
        # Keep the supplemental minifigure set even when Studio's mapping
        # also lists the design, since the packaged LDraw library may not.
        if name not in known or exact_mould or group == "Minifigs":
            selected[name] = (group, category, description)
    if only:
        selected = {name: selected[name] for name in only}
    library = Library(studio / "ldraw")
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output) as temporary:
        stage = Path(temporary)
        manifest = []
        failures = []
        bundle = None
        count = 0
        group_count = 0
        previous_group = None
        central_size = 22
        try:
            for name, (group, category, description) in sorted(selected.items(), key=lambda item: (item[1][0], item[0])):
                try:
                    fbx = make_fbx(library, name)
                    member = name[:-4] + ".fbx"
                    # DEFLATE overhead is bounded by this conservative estimate.
                    # Include local and central ZIP headers and the end record.
                    local_bound = len(fbx) + len(fbx) // 100 + 256 + len(member)
                    central_entry = 46 + len(member)
                    if local_bound + central_entry + 22 >= LIMIT:
                        raise ValueError("Single FBX exceeds bundle size budget")
                    if group != previous_group:
                        if bundle:
                            bundle.close()
                        bundle = None
                        group_count = 0
                        previous_group = group
                    if bundle is None or bundle.fp.tell() + local_bound + central_size + central_entry >= LIMIT:
                        if bundle:
                            bundle.close()
                        count += 1
                        group_count += 1
                        bundle_name = f"StudioParts-{group}-{group_count:03d}.zip"
                        bundle = zipfile.ZipFile(stage / bundle_name, "w", compression=zipfile.ZIP_DEFLATED,
                                                 compresslevel=6, allowZip64=False)
                        central_size = 22
                    bundle.writestr(zipfile.ZipInfo(member), fbx, compress_type=zipfile.ZIP_DEFLATED, compresslevel=6)
                    central_size += central_entry
                    manifest.append((name, bundle_name, hashlib.sha256(fbx).hexdigest()[:16], category, description))
                except (ValueError, IndexError) as error:
                    failures.append((name, str(error)))
        finally:
            if bundle:
                bundle.close()

        for archive in stage.glob("*.zip"):
            if archive.stat().st_size >= LIMIT:
                raise RuntimeError(f"{archive.name} exceeds the archive limit")
            meta(archive)
        for name, header, records in (
            ("StudioParts.tsv", ("part", "archive", "revision", "category", "description"), manifest),
            ("StudioParts-skipped.tsv", ("part", "reason"), failures),
        ):
            path = stage / name
            with path.open("w", encoding="utf-8", newline="") as stream:
                writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
                writer.writerow(header)
                writer.writerows(records)
            meta(path)
        if not manifest:
            raise RuntimeError("No parts converted")
        for path in output.glob("StudioParts-*.zip*"):
            if path.is_file() and not (stage / path.name).exists():
                path.unlink()
        for path in stage.iterdir():
            path.replace(output / path.name)
    print(f"Converted {len(manifest)} parts into {count} bundles. Skipped {len(failures)} of {len(selected)} candidates.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--studio", type=Path, default=Path("/Applications/Studio 2.0"))
    parser.add_argument("--data", type=Path, default=Path(__file__).resolve().parents[1] / "Data")
    parser.add_argument("--output", type=Path, help="Defaults to the importer Data directory")
    parser.add_argument("--only", nargs="+", help="Build only these candidate .dat files into a separate output folder")
    options = parser.parse_args()
    if options.only and not options.output:
        parser.error("--only requires a separate --output folder")
    build(options.studio, options.data, options.output or options.data, options.only)
