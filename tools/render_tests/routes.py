"""Routes through the game's menus, each ending on a known screen or raising RunBroken.

A screen is known by a string it looks up as it opens (--fm4_log_strings): its title, or the
name of the item that has focus. Menus drop presses during their transitions, so a list is
first pushed against its edge (extra presses there do nothing) and then counted from it, and an
A that opened the wrong screen is backed out of and tried again. Career, pause and pre-race
menus hide after some seconds idle and take the next press to come back: every step here
presses within a second or two of the screen appearing.
"""
import re
import time

from .game import FileOpened, Log, RunBroken, String

MAIN_MENU = String("AUTOVISTA")
CAREER = String("UPGRADE")
CARS = String("TAKE PHOTO")
DEALER = String("ALL AFFORDABLE CARS")
MANUFACTURERS = String("MANUFACTURER SELECT")
CAR_SELECT = String("CAR SELECT")
SHOWROOM = String("COLOR SELECT")
ASSISTS = String("ASSISTS & DIFFICULTY")
ENVIRONMENTS = String("ENVIRONMENT SELECT")
TRACKS = String("TRACK SELECT")
PRE_RACE = String("PRE-RACE")
DRIVING = Log(r"^Input context: driving$")
RACE_SHOWN = Log(r"^Present interval: 1$")
PAUSE = String("PAUSE MENU")
QUIT_CONFIRM = Log(r"^String: Are you sure you want to quit the race\?")
POST_RACE = String("POST-RACE")
BUY_CONFIRM = String("BUY CAR?")
GARAGE = String("SELL PRICE")
ABARTH = Log(r"^String: In 1909, Italian-Australian Carlo Abarth")
ABARTH_500 = String("500 esseesse")
THUMBNAIL = FileOpened("\\thumbnails\\thumbnail_")
_THUMBNAIL_NUMBER = re.compile(r"\\thumbnails\\thumbnail_(\d+)\.xdc$", re.IGNORECASE)

MOVE_GAP = 0.3
SCREEN_TIMEOUT = 8
LOAD_TIMEOUT = 120


def _seen(session, signal, since):
    session.poll()
    return signal.ready(session, session.lines[since:])


def _moves(session, controls):
    for control in controls:
        session.press(control)
        time.sleep(MOVE_GAP)


def _new_strings(session, since):
    """Strings logged since a line that the lines before it did not log: another screen's.
    The screen in view logs its own again when its focus moves."""
    seen = {line.text for line in session.lines[max(0, since - 400):since]}
    return [line.text for line in session.lines[since:]
            if line.text.startswith("String: ") and line.text not in seen]


def enter(session, moves, expected, back_to, step, attempts=3):
    """Makes the moves, presses A and waits for the expected screen. A press the game dropped
    leaves the screen as it was: A again. Another screen: back out to back_to, then again (a B
    on the screen still in view would leave it, from the main menu to the title)."""
    for attempt in range(attempts):
        _moves(session, moves)
        session.poll()
        since = len(session.lines)
        try:
            session.press_until("A", expected, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                                step=step)
            return
        except RunBroken:
            if attempt == attempts - 1:
                raise
            if _new_strings(session, since):
                session.press_until("B", back_to, timeout=20, every=3, step=f"{step}: back out")
    raise RunBroken(step, "unreachable")


def focus(session, move, signal, presses, since, step):
    """Moves focus one press at a time until the focused item's string appears."""
    if _seen(session, signal, since):
        return
    for _ in range(presses):
        mark = len(session.lines)
        session.press(move)
        deadline = time.monotonic() + 1.5
        while time.monotonic() < deadline:
            if _seen(session, signal, mark):
                return
            time.sleep(0.1)
    raise RunBroken(step, f"no {signal!r} after {presses} presses of {move}")


def to_main_menu(session):
    """From launch: the title, then the main menu with its scene drawn."""
    session.press_until("A", MAIN_MENU, timeout=LOAD_TIMEOUT, every=2, step="title")
    time.sleep(3)


def back_to_main_menu(session):
    """B until the main menu: one B more there would go to the title."""
    session.press_until("B", MAIN_MENU, timeout=90, every=3, step="back to the main menu")
    time.sleep(1)


def _abarth_500(session, step):
    """From a manufacturer select: Abarth, then its 500 esseesse."""
    since = len(session.lines)
    _moves(session, ["LEFT"] * 6)
    focus(session, "RIGHT", ABARTH, 4, since, f"{step}: Abarth")
    session.poll()
    since = len(session.lines)
    session.press_until("A", CAR_SELECT, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step=f"{step}: car select")
    focus(session, "RIGHT", ABARTH_500, 3, since, f"{step}: 500 esseesse")


def showroom(session):
    """Main menu -> Free Play -> Quick Race -> Abarth 500: the showroom (colour select)."""
    enter(session, ["LEFT"] * 6 + ["RIGHT"] * 3, String("QUICK RACE"), MAIN_MENU, "free play")
    enter(session, ["LEFT"] * 3, MANUFACTURERS, String("QUICK RACE"), "quick race")
    _abarth_500(session, "quick race")
    session.press_until("A", SHOWROOM, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="showroom")


def quick_race(session, environment_steps=0, environment=None):
    """Main menu -> the showroom -> assists -> environment -> track -> GO RACE -> START RACE.
    Ends at GO; environment names the expected one, if given."""
    showroom(session)
    time.sleep(1)
    session.press_until("A", ASSISTS, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT, step="assists")
    session.press_until("A", ENVIRONMENTS, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="environment select")
    # Environment select is back at its first entry after every car change. It looks up every
    # environment's name as it opens: past the log's one-second repeat window, track select's
    # lookup of the chosen one is logged again.
    _moves(session, ["RIGHT"] * environment_steps)
    time.sleep(1.2)
    since = len(session.lines)
    session.press_until("A", TRACKS, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="track select")
    if environment and not _seen(session, String(environment), since):
        raise RunBroken("track select", f"not the environment {environment}")
    session.press("A")
    session.wait(PRE_RACE, LOAD_TIMEOUT, step="race load")
    session.press_until("A", DRIVING, timeout=30, every=3, step="start race")
    # The grid fades in at full presentation rate about a second before GO.
    session.wait(RACE_SHOWN, 60, step="race fade-in")
    time.sleep(1)


def drive(session, seconds):
    """Full throttle, steering held straight."""
    session.hold("RT=1 LX=0", seconds)
    time.sleep(seconds)


def quit_race(session):
    """Pause, QUIT RACE, OK, CONTINUE: back at track select, then out to the main menu."""
    session.press_until("START", PAUSE, timeout=20, every=3, step="pause")
    _moves(session, ["DOWN"] * 8)
    session.press_until("A", QUIT_CONFIRM, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="quit race")
    session.press("A")
    session.wait(POST_RACE, 60, step="post-race")
    session.press_until("A", TRACKS, timeout=60, every=4, step="continue")
    back_to_main_menu(session)


def _career_cars(session, step):
    enter(session, ["LEFT"] * 6, CAREER, MAIN_MENU, f"{step}: career")
    enter(session, ["UP"] * 5 + ["DOWN"], CARS, CAREER, f"{step}: cars")


def buy_car(session):
    """Main menu -> career -> CARS -> BUY CARS -> Abarth 500, white, for credits. Returns the
    new thumbnail's slot number; ends back in the career's CARS menu."""
    _career_cars(session, "buy car")
    enter(session, ["UP"] * 4 + ["DOWN"], DEALER, CARS, "buy cars")
    _abarth_500(session, "buy car")
    session.press_until("A", SHOWROOM, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="buy car: colour select")
    time.sleep(1)
    session.press_until("A", BUY_CONFIRM, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="buy car: buy")
    since = len(session.lines)
    session.press("A")
    session.wait(THUMBNAIL, 30, step="buy car: thumbnail")
    numbers = [int(match.group(1)) for line in session.lines[since:]
               for match in [_THUMBNAIL_NUMBER.search(line.text)] if match]
    session.wait(CARS, 30, step="buy car: back in the career")
    return numbers[-1]


def garage(session, from_main_menu=True):
    """The career's MY CARS screen with the car last bought selected."""
    if from_main_menu:
        _career_cars(session, "garage")
    enter(session, ["UP"] * 4, GARAGE, CARS, "garage")


def dealer_showroom(session):
    """From the career's CARS menu: the dealer's colour select of the Abarth 500, not bought
    (after a purchase, where the thirty-first session's magenta cars showed); ends back in
    the CARS menu once leave_dealer_showroom is called."""
    enter(session, ["UP"] * 4 + ["DOWN"], DEALER, CARS, "dealer showroom: buy cars")
    _abarth_500(session, "dealer showroom")
    session.press_until("A", SHOWROOM, timeout=SCREEN_TIMEOUT, every=SCREEN_TIMEOUT,
                        step="dealer showroom")


def leave_dealer_showroom(session):
    session.press_until("B", CARS, timeout=40, every=3, step="leave the dealer")
