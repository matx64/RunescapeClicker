import tkinter as tk
import time
from bindglobal import BindGlobal
from random import random
from pynput.mouse import Button, Controller as MouseController
from pynput.keyboard import Listener, Key, Controller as KeyboardController


class MainApplication(tk.Frame):
    from ._mouse import handle_mouse_action, set_mouse_button, add_click
    from ._keyboard import handle_keyboard_action, set_keyboard_key, add_keyboard_press
    from ._delay import handle_new_delay, add_delay
    from ._stop import handle_stop, add_stop

    def __init__(self, master=None):
        super().__init__(master)
        self.master = master
        self.bg = BindGlobal(widget=self.master)
        self.mouse = MouseController()
        self.keyboard = KeyboardController()
        self.mouse_position = (100, 100)
        self.mouse_btn = "left"
        self.keyboard_key = "space"
        self.delay_amount = 0
        self.exec_order = []
        self.stop_option = 1
        self.start_time = None
        self.stop_time = 0
        self.continue_exec = True
        self.funcs = {"click_mouse_left": self.click_mouse_left,
                      "click_mouse_right": self.click_mouse_right, "delay": self.delay, "press_key": self.press_key}
        self.pack()
        self.load_header()
        self.load_info_frame()
        self.position_input = tk.Entry(self)
        self.position_input.destroy()

        self.bg.gbind('<KeyPress-F1>', self.get_mouse_position)
        self.bg.gbind('<KeyPress-F2>', self.stop_exec)

    def get_mouse_position(self, event):
        if self.position_input.winfo_exists():
            self.mouse_position = self.mouse.position
            self.position_input.delete(0, tk.END)
            self.position_input.insert(0, str(self.mouse_position))

    def stop_exec(self, event):
        self.continue_exec = False

    def load_header(self):
        self.header = tk.Frame(self)

        self.mouse_action_btn = tk.Button(
            self.header, text="Add Mouse Click", command=self.handle_mouse_action, fg="white",  bg="#3772FF")
        self.mouse_action_btn.pack(
            side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.keyboard_action_btn = tk.Button(
            self.header, text="Add Keyboard Press", command=self.handle_keyboard_action, fg="white",  bg="#B30089")
        self.keyboard_action_btn.pack(
            side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.delay_btn = tk.Button(
            self.header, text="Add Delay", command=self.handle_new_delay, fg="black",  bg="#F2BB05")
        self.delay_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.set_stop_btn = tk.Button(
            self.header, text="Set Stop", command=self.handle_stop, fg="white",  bg="#F9564F")
        self.set_stop_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.header.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def load_info_frame(self):
        self.info_frame = tk.Frame(self, background="#acacad")
        self.info_footer = tk.Frame(self)

        self.info_header = tk.Label(self.info_frame, text="Loop Order:")
        self.info_header.pack(padx=5, pady=5, ipadx=5, ipady=5)

        self.info_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

        self.stop_info = tk.Label(self.info_footer)
        self.stop_info.pack(padx=2, pady=2, ipadx=2, ipady=2)

        self.start_btn = tk.Button(
            self.info_footer, text="START", command=self.normal_start, fg="black", bg="#03DD5E")
        self.start_btn.destroy()

        self.info_footer.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def add_action_info(self, type):
        action_label = tk.Label(self.info_frame)

        if type == "mouse_click":
            action_label.config(bg="#3772FF")
            action_label.config(fg="white")

            if self.mouse_btn == "left":
                action_label.config(
                    text="Left Click on {0}".format(str(self.mouse_position)))
            else:
                action_label.config(
                    text="Right Click on {0}".format(str(self.mouse_position)))

        elif type == "keyboard_press":
            action_label.config(bg="#B30089")
            action_label.config(fg="white")
            action_label.config(text=f"Press {self.keyboard_key.capitalize()}")

        elif type == "delay":
            action_label.config(bg="#F2BB05")
            action_label.config(fg="black")
            action_label.config(text="{0}s Delay".format(self.delay_amount))

        action_label.pack(padx=2, pady=2, ipadx=2, ipady=2)

    def delay(self, interval):
        time.sleep(interval + (random() * 0.1))

    def click_mouse_left(self, position):
        self.mouse.position = position
        self.mouse.click(Button.left, 1)

    def click_mouse_right(self, position):
        self.mouse.position = position
        self.mouse.click(Button.right, 1)

    def press_key(self, key):
        if key == "space":
            key = Key.space

        self.keyboard.press(key)
        self.keyboard.release(key)

    def before_start(self):
        self.continue_exec = True

        if self.stop_option == 2:
            self.start_time = time.time()
            self.timed_start()
        else:
            self.normal_start()

    def timed_start(self):
        current_time = time.time()
        if(self.continue_exec and current_time - self.start_time <= self.stop_time):
            for action in self.exec_order:
                self.funcs[action[0]](action[1])
            self.master.after(1, self.timed_start)

    def normal_start(self):
        if(self.continue_exec):
            for action in self.exec_order:
                self.funcs[action[0]](action[1])
            self.master.after(1, self.normal_start)
