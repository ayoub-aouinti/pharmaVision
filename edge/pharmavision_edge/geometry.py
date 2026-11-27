"""Géométrie en coordonnées normalisées (0..1), origine en haut à gauche de l'image."""

Point = tuple[float, float]


def point_in_polygon(point: Point, polygon: list[Point]) -> bool:
    """Lancer de rayon horizontal : un nombre impair de côtés traversés signifie « dedans »."""
    x, y = point
    inside = False
    for (x1, y1), (x2, y2) in zip(polygon, polygon[1:] + polygon[:1]):
        if (y1 > y) != (y2 > y) and x < x1 + (y - y1) / (y2 - y1) * (x2 - x1):
            inside = not inside
    return inside


def side(a: Point, b: Point, p: Point) -> float:
    """Positif d'un côté de la droite AB, négatif de l'autre."""
    return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])


def crossing(a: Point, b: Point, before: Point, after: Point) -> int:
    """
    Franchissement du segment AB entre deux positions : +1 vers le côté positif, -1 vers le côté négatif,
    0 sinon. Pour une ligne tracée de gauche à droite, +1 correspond à un déplacement vers le bas.
    """
    was, now = side(a, b, before), side(a, b, after)
    if (was > 0) == (now > 0):
        return 0
    # La ligne n'est franchie que si ses deux extrémités sont de part et d'autre du déplacement.
    if (side(before, after, a) > 0) == (side(before, after, b) > 0):
        return 0
    return 1 if now > 0 else -1
