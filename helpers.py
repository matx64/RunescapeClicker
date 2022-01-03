import time
from random import random
from pynput.mouse import Button, Controller as MouseController
from pynput.keyboard import Listener, Key, Controller as KeyboardController

mouse = MouseController()
keyboard = KeyboardController()


def delay(interval):
    time.sleep(interval + (random() * 0.1))


def mouse_action(key):
    mouse.click(key)


def keyboard_action(key):
    keyboard.click(key)
