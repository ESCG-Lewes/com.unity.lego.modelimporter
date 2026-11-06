#!/usr/bin/env python3
"""Convert supported Studio connection records to the importer's LEGO primitive XML."""
import argparse
import collections
import csv
import math
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET
import zipfile

from build_studio_parts import LIMIT, PART_NAME, Library, meta, rows

LDU = 0.04
PLANAR_TYPES = {
    2: {-1: 29, 0: 22, 10: 15, 20: 7},
    3: {-1: 29, 0: 23, 3: 18, 10: 0, 20: 1},
}
# Lengths include the common type, transform and position header.
RECORD_LENGTHS = {0: 60, 1: 52, 4: 54, 6: 69, 8: 59}


def attributes(matrix, position):
    """Convert the Studio basis into the XML basis expected by PartUtility."""
    if not all(math.isfinite(value) for value in (*matrix, *position)):
        raise ValueError("Non-finite Studio transform")
    rows = [matrix[i:i + 3] for i in range(0, 9, 3)]
    if any(abs(sum(value * value for value in row) - 1) > 0.002 for row in rows):
        raise ValueError("Invalid Studio rotation")
    rows = list(zip(*rows))
    # Unity's XML reader reflects X. Negating the equivalent rotation axis
    # components on Y and Z compensates for that reflection.
    x, y, z = (rows[2][1]-rows[1][2], rows[0][2]-rows[2][0], rows[1][0]-rows[0][1])
    cosine = max(-1, min(1, (sum(rows[i][i] for i in range(3)) - 1) / 2))
    angle = math.acos(cosine)
    if angle < 0.001 or (cosine > 0 and math.hypot(x, y, z) < 0.00001):
        x, y, z = 0, 1, 0
        angle = 0
    elif math.pi - angle < 0.002 or (cosine < 0 and math.hypot(x, y, z) < 0.00001):
        i = max(range(3), key=lambda j: rows[j][j])
        axis = [0.0] * 3
        axis[i] = math.sqrt(max(0, (rows[i][i] + 1) / 2))
        if axis[i] < 0.00001:
            raise ValueError("Invalid Studio half-turn")

        for j in range(3):
            if j != i:
                axis[j] = (rows[i][j] + rows[j][i]) / (4 * axis[i])
        x, y, z = axis
    length = math.hypot(x, y, z)
    if length < 0.00001:
        raise ValueError("Invalid Studio rotation axis")
    return {
        "tx": number(position[0] * LDU), "ty": number(-position[1] * LDU), "tz": number(-position[2] * LDU),
        "angle": number(math.degrees(angle)), "ax": number(x / length),
        "ay": number(-y / length), "az": number(-z / length),
    }


def number(value):
    return format(value, ".8g")


def read_connections(raw, node):
    unsupported = set()
    offset = 0
    while offset < len(raw):
        if len(raw) - offset < 52:
            unsupported.add("truncated connection")
            break
        kind, subtype, *values = struct.unpack_from("<HH12f", raw, offset)
        try:
            transform = attributes(values[:9], values[9:])
        except ValueError:
            unsupported.add("invalid rotation, remainder unread")
            break
        if kind in PLANAR_TYPES:
            if len(raw) - offset < 56:
                unsupported.add("truncated grid")
                break
            height, width = struct.unpack_from("<HH", raw, offset + 52)
            count = (width + 1) * (height + 1)
            size = 56 + count * 4
            if size > len(raw) - offset:
                unsupported.add("truncated grid")
                break
            mapping = PLANAR_TYPES[kind]
            features = []
            for index in range(count):
                feature, quadrants = struct.unpack_from("<hh", raw, offset + 56 + 4 * index)
                if feature not in mapping or not 0 <= quadrants <= 4:
                    unsupported.add(f"grid {kind}/{feature}")
                    features.append("29:0")
                    continue
                native = mapping[feature]
                if kind == 2 and feature == 0 and quadrants == 4:
                    native = 23
                # A lone round underside tube uses the ordinary tube type.
                if kind == 2 and feature == 20 and width == height == 2:
                    native = 5
                features.append(f"{native}:{quadrants}:1")
            if any(int(feature.split(":")[0]) in {0, 1, 5, 7, 15, 18} for feature in features):
                field = ET.SubElement(node, "Custom2DField", transform,
                                      type=str(20 + kind), width=str(width), height=str(height))
                field.text = ",".join(features)
        elif kind in RECORD_LENGTHS:
            size = RECORD_LENGTHS[kind]
            if size > len(raw) - offset:
                unsupported.add("truncated connection")
                break
            if kind == 0 and subtype in {12, 13}:
                start, end, length, grabbing, _ = struct.unpack_from("<BBfBB", raw, offset + 52)
                if math.isfinite(length) and length > 0 and start in (0, 1) and end in (0, 1):
                    ET.SubElement(node, "Axel", transform,
                                  type="6" if subtype == 12 else "7", length=number(length * LDU),
                                  startCapped=str(start), endCapped=str(end),
                                  grabbing="1" if subtype == 12 and grabbing == 1 else "0")
                else:
                    unsupported.add(f"axle {subtype}")
            else:
                unsupported.add(f"connection {kind}/{subtype}")
        else:
            unsupported.add(f"connection {kind}/{subtype}, remainder unread")
            break
        offset += size
    return unsupported


STUD_PRIMITIVES = {"stud.dat": 0, "stud2.dat": 1, "stud6.dat": 1}
IDENTITY = ((1, 0, 0), (0, 1, 0), (0, 0, 1))


def transform_vector(matrix, vector):
    return tuple(sum(a * b for a, b in zip(row, vector)) for row in matrix)


def studs(library, name, basis=IDENTITY, origin=(0, 0, 0), parents=()):
    if name in parents or len(parents) >= 64:
        return
    primitive = name.removeprefix("48/").removeprefix("8/")
    if primitive in STUD_PRIMITIVES:
        # Reject scaled, sheared, or mirrored geometry. Only infer ordinary studs.
        columns = tuple(zip(*basis))
        if any(abs(sum(a * b for a, b in zip(basis[i], basis[j])) - (i == j)) > 0.001
               for i in range(3) for j in range(3)):
            return
        a, b, c = basis
        determinant = (a[0] * (b[1]*c[2] - b[2]*c[1]) -
                       a[1] * (b[0]*c[2] - b[2]*c[0]) +
                       a[2] * (b[0]*c[1] - b[1]*c[0]))
        if determinant < 0:
            return
        yield STUD_PRIMITIVES[primitive], columns, origin
        return
    try:
        commands = library.commands(name)
    except (ValueError, IndexError):
        return
    for command, payload in commands:
        if command == "ref":
            values, child = payload
            offset = transform_vector(basis, values[:3])
            child_origin = tuple(a + b for a, b in zip(origin, offset))
            local = tuple(values[i:i+3] for i in (3, 6, 9))
            child_basis = tuple(zip(*(transform_vector(basis, column) for column in zip(*local))))
            yield from studs(library, child, child_basis, child_origin, parents + (name,))


def infer_studs(library, name, node):
    found = set()
    for kind, columns, center in studs(library, name):
        key = tuple(round(value, 4) for value in (*center, *columns[1]))
        if key in found:
            continue
        found.add(key)
        offset = transform_vector(tuple(zip(*columns)), (-10, 0, 10))
        corner = tuple(a + b for a, b in zip(center, offset))
        transform = attributes(tuple(value for column in columns for value in column), corner)
        field = ET.SubElement(node, "Custom2DField", transform, type="23", width="2", height="2")
        field.text = f"29:0,29:0,29:0,29:0,{kind}:4:1,29:0,29:0,29:0,29:0"
    return len(found)


def read_boxes(raw, node):
    # Some .col files have a short binary header preceding the text.
    for match in re.finditer(rb"(?:^|[\r\n]|\x00)9[ \t]+0[ \t]+([^\r\n]+)", raw):
        tokens = match[1].split()
        if len(tokens) not in (15, 16):
            continue
        try:
            values = [float(token) for token in tokens[:15]]
        except ValueError:
            continue
        sizes = values[12:]
        if not all(math.isfinite(size) and size > 0 for size in sizes):
            continue
        try:
            transform = attributes(values[:9], values[9:12])
        except ValueError:
            continue
        ET.SubElement(node, "Box", transform,
                      **{key: number(size * LDU) for key, size in zip(("sX", "sY", "sZ"), sizes)})


def build(studio, data):
    ldraw = studio / "ldraw"
    library = Library(ldraw)
    coverage = collections.Counter()
    catalogue = list(rows(studio / "data" / "StudioPartDefinition2.txt"))
    names = {row["Studio ItemNo"]: row["LDraw ItemNo"].strip().lower() for row in catalogue}
    decorated_bases = {}
    for row in catalogue:
        name = row["LDraw ItemNo"].strip().lower()
        base = names.get(row["BaseStudioItemNo"], "")
        if (name != base and PART_NAME.fullmatch(base) and
                row["IsDecorated"].strip().casefold() == "true" and "sticker" not in row["Description"].casefold()):
            decorated_bases[name] = Path(base).stem
    target = data / "StudioConnections.zip"
    temporary = target.with_suffix(".tmp")
    report = []
    with (data / "StudioParts.tsv").open(encoding="utf-8", newline="") as manifest, \
         zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for row in csv.DictReader(manifest, delimiter="\t"):
            name = Path(row["part"]).stem
            root = ET.Element("LEGOPrimitive")
            collision = ET.SubElement(root, "Collision")
            conn = ET.SubElement(root, "Connectivity")
            bounding = ET.SubElement(root, "Bounding")
            ET.SubElement(bounding, "AABB", {axis: "0" for axis in
                          ("minX", "minY", "minZ", "maxX", "maxY", "maxZ")})
            collider = ldraw / "collider" / (name + ".col")
            connection = ldraw / "connectivity" / (name + ".conn")
            base = decorated_bases.get(row["part"])
            if base:
                if not collider.is_file():
                    collider = ldraw / "collider" / (base + ".col")
                if not connection.is_file():
                    connection = ldraw / "connectivity" / (base + ".conn")
            if collider.is_file():
                read_boxes(collider.read_bytes(), collision)
            unsupported = read_connections(connection.read_bytes(), conn) if connection.is_file() else {"no Studio connection file"}
            if not collider.is_file():
                unsupported.add("no Studio collider file")
            elif not len(collision):
                unsupported.add("no supported collision boxes")
            # Preserve authored connections. Infer ordinary studs only for parts
            # with usable body collision boxes and no supported authored fields.
            inferred = infer_studs(library, row["part"], conn) if len(collision) and not len(conn) else 0
            report.append((row["part"], len(collision), len(conn.findall("Custom2DField")),
                           len(conn.findall("Axel")), inferred, "; ".join(sorted(unsupported))))
            coverage["inferred studs"] += inferred
            coverage["parts with inferred studs"] += bool(inferred)
            coverage["studio connection files"] += connection.is_file()
            if len(collision) or len(conn):
                archive.writestr(name + ".xml", ET.tostring(root, encoding="utf-8"))
            coverage["parts"] += 1
            coverage["collision boxes"] += bool(len(collision))
            coverage["supported connections"] += bool(len(conn))
            coverage["both"] += bool(len(collision) and len(conn))
    if temporary.stat().st_size >= LIMIT:
        temporary.unlink()
        raise RuntimeError("Studio connections archive exceeds the bundle limit")
    temporary.replace(target)
    meta(target)
    report_path = data / "StudioConnections.tsv"
    with report_path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
        writer.writerow(("part", "boxes", "planar fields", "clip/bar fields", "inferred studs", "limitations"))
        writer.writerows(report)
    meta(report_path)
    print(dict(coverage))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--studio", type=Path, default=Path("/Applications/Studio 2.0"))
    parser.add_argument("--data", type=Path, default=Path(__file__).resolve().parents[1] / "Data")
    args = parser.parse_args()
    build(args.studio, args.data)
