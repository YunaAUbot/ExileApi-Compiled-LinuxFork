#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/extensions/shape.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int has_title(Display *dpy, Window window, const char *title) {
    char *name = NULL;
    if (XFetchName(dpy, window, &name) && name) {
        int match = !strcmp(name, title);
        XFree(name);
        return match;
    }
    return 0;
}

// _NET_CLIENT_LIST contains the real XWayland top-level windows. A recursive
// title search can otherwise select Wine's same-titled child window, which
// makes ShapeInput changes ineffective for the visible overlay.
static Window find_client_window(Display *dpy, Window root, const char *title) {
    Atom client_list = XInternAtom(dpy, "_NET_CLIENT_LIST", True);
    Atom actual_type;
    int actual_format;
    unsigned long item_count, bytes_after;
    unsigned char *data = NULL;
    if (client_list != None && XGetWindowProperty(dpy, root, client_list, 0, 4096, False,
        XA_WINDOW, &actual_type, &actual_format, &item_count, &bytes_after, &data) == Success && data) {
        Window *windows = (Window *)data;
        for (unsigned long i = 0; i < item_count; ++i) {
            if (has_title(dpy, windows[i], title)) {
                Window result = windows[i];
                XFree(data);
                return result;
            }
        }
        XFree(data);
    }

    // Fallback for WMs that do not expose EWMH client lists.
    Window root_return, parent; Window *children = NULL; unsigned int child_count = 0;
    if (!XQueryTree(dpy, root, &root_return, &parent, &children, &child_count)) return None;
    Window found = None;
    for (unsigned int i = 0; i < child_count && !found; ++i) {
        if (has_title(dpy, children[i], title)) found = children[i];
    }
    if (children) XFree(children);
    return found;
}

int main(int argc, char **argv) {
    FILE *log = fopen("/tmp/exileapi-x11-input-shape.log", "a");
    if (argc != 3 || (strcmp(argv[2], "passthrough") && strcmp(argv[2], "interactive"))) return 2;
    Display *dpy = XOpenDisplay(NULL);
    if (!dpy) { if (log) { fputs("no-display\n", log); fclose(log); } return 3; }
    int event_base, error_base;
    if (!XShapeQueryExtension(dpy, &event_base, &error_base)) { XCloseDisplay(dpy); return 4; }
    Window window = find_client_window(dpy, DefaultRootWindow(dpy), argv[1]);
    if (window == None) { if (log) { fprintf(log, "window-not-found title=%s\n", argv[1]); fclose(log); } XCloseDisplay(dpy); return 5; }
    if (!strcmp(argv[2], "passthrough"))
        XShapeCombineRectangles(dpy, window, ShapeInput, 0, 0, NULL, 0, ShapeSet, Unsorted);
    else
        XShapeCombineMask(dpy, window, ShapeInput, 0, 0, None, ShapeSet);
    XSync(dpy, False);
    if (log) { fprintf(log, "applied title=%s mode=%s window=0x%lx\n", argv[1], argv[2], window); fclose(log); }
    XCloseDisplay(dpy);
    return 0;
}
