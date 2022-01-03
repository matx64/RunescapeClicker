import tkinter as tk
from tkinter.constants import SUNKEN
from pynput.mouse import Button, Controller as MouseController
from pynput.keyboard import Listener, Key, Controller as KeyboardController


class MainApplication(tk.Frame):
    def __init__(self, master=None):
        super().__init__(master)
        self.master = master
        self.mouse = MouseController()
        self.mouse_position = (100, 100)
        self.mouse_btn = "left"
        self.keyboard_btn = "space"
        self.delay = 0
        self.exec_order = []
        self.pack()
        self.load_header()
        self.load_info_frame()
        self.new_action_frame = tk.Frame(self)
        self.new_action_frame.destroy()
        self.master.bind('<KeyPress-F1>', self.on_press)

    def on_press(self, event):
        self.mouse_position = self.mouse.position
        if self.new_action_frame.winfo_exists():
            self.position_input.delete(0, tk.END)
            self.position_input.insert(0, str(self.mouse_position))

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
            self.header, text="Set Stop", command=self.handle_new_delay, fg="white",  bg="#F9564F")
        self.set_stop_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.header.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def load_info_frame(self):
        self.info_frame = tk.Frame(self)

        self.info_header = tk.Label(self.info_frame, text="Order of Actions:")
        self.info_header.pack(padx=5, pady=5, ipadx=5, ipady=5)

        self.info_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

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
            if self.keyboard_btn == "space":
                action_label.config(
                    text="Press Space")

        elif type == "delay":
            action_label.config(bg="#F2BB05")
            action_label.config(fg="black")
            action_label.config(text="{0}s Delay".format(self.delay))

        action_label.pack(padx=2, pady=2, ipadx=2, ipady=2)

    def handle_mouse_action(self):
        self.mouse_action_btn.config(relief="sunken")
        self.mouse_action_btn.config(state="disabled")

        self.new_action_frame = tk.Frame(self)

        self.header_txt = tk.Label(
            self.new_action_frame, text="Press F1 to get mouse position")
        self.header_txt.pack(fill=tk.X, padx=5, pady=5, ipadx=5, ipady=5)

        self.mouse_btn = "left"
        self.mouseleft_btn = tk.Button(
            self.new_action_frame, text="Mouse Left", command=self.set_mouse_left, relief="sunken")
        self.mouseleft_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.mouseright_btn = tk.Button(
            self.new_action_frame, text="Mouse Right", command=self.set_mouse_right)
        self.mouseright_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.position_label = tk.Label(
            self.new_action_frame, text="on Position:")
        self.position_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.position_input = tk.Entry(self.new_action_frame, width=25)
        self.position_input.insert(0, str(self.mouse_position))
        self.position_input.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.add_click_btn = tk.Button(
            self.new_action_frame, text="Add", command=self.add_click, fg="black", bg="#03DD5E")
        self.add_click_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.new_action_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def set_mouse_left(self):
        if self.mouse_btn != "left":
            self.mouseleft_btn.config(relief="sunken")
            self.mouseright_btn.config(relief="raised")
            self.mouse_btn = "left"

    def set_mouse_right(self):
        if self.mouse_btn != "right":
            self.mouseright_btn.config(relief="sunken")
            self.mouseleft_btn.config(relief="raised")
            self.mouse_btn = "right"

    def add_click(self):
        self.mouse_action_btn.config(relief="raised")
        self.mouse_action_btn.config(state="normal")
        self.new_action_frame.destroy()
        self.add_action_info("mouse_click")
        self.exec_order.append(
            (f"mouse_{self.mouse_btn}", self.mouse_position))
        print(self.exec_order)

    def handle_keyboard_action(self):
        self.new_action_frame = tk.Frame(self)
        self.key1_btn = tk.Button(
            self.new_action_frame, text="1", command=self.set1Key)

    def handle_new_delay(self):
        pass
