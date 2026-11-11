# Vector PxeVice icon on a 1024 unit canvas, in the same style and colours as the LogVice and PolicyVice icons:
# the three lines on the left, and a reset arrow around a network boot arrow where LogVice has its magnifier.
NAVY, CYAN, PINK = '#011133', '#00EEFE', '#FF199A'

def at_least(size, base, table):
    px = table.get(size, 0)
    return max(base, px * 1024.0 / size)

def app(size):
    w = at_least(size, 40, {16: 1.3, 20: 1.4, 24: 1.5, 32: 1.6, 40: 1.8, 48: 1.9, 64: 2.3})
    lines = [(155, 420, 358), (155, 390, 483), (155, 340, 608)] if size > 16 else [(155, 400, 400), (155, 340, 590)]
    parts = ['<rect x="20" y="20" width="984" height="984" rx="150" fill="%s"/>' % NAVY]
    for x0, x1, y in lines:
        parts.append('<line x1="%g" y1="%g" x2="%g" y2="%g" stroke="%s" stroke-width="%g" stroke-linecap="round"/>' % (x0 + w / 2, y, x1, y, CYAN, w))
    # Reset arc: centre 662,492, radius 172, from 330 degrees clockwise round to the top, with the arrowhead there.
    arc = 'M 810.96 406 A 172 172 0 1 1 662 320'
    head = 'M 614 272 L 662 320 L 614 368'
    parts.append('<path d="%s" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round"/>' % (arc, PINK, w))
    parts.append('<path d="%s" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"/>' % (head, PINK, w))
    # Network boot: an arrow down onto a line. Small sizes keep only the arrow.
    if size > 20:
        parts.append('<path d="M 662 420 L 662 548 M 616 502 L 662 548 L 708 502" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"/>' % (CYAN, w))
        parts.append('<line x1="606" y1="600" x2="718" y2="600" stroke="%s" stroke-width="%g" stroke-linecap="round"/>' % (CYAN, w))
    else:
        parts.append('<path d="M 662 410 L 662 570 M 600 508 L 662 570 L 724 508" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"/>' % (CYAN, w))
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">' + ''.join(parts) + '</svg>'
