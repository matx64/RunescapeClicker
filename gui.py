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
        self.keyboard_key = "space"
        self.delay = 0
        self.exec_order = []
        self.pack()
        self.load_header()
        self.load_info_frame()
        self.position_input = tk.Entry(self)
        self.position_input.destroy()
        self.master.bind('<KeyPress-F1>', self.on_press)

    def on_press(self, event):
        self.mouse_position = self.mouse.position
        if self.position_input.winfo_exists():
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

        self.info_header = tk.Label(self.info_frame, text="Order of Actions")
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
            action_label.config(text=f"Press {self.keyboard_key.capitalize()}")

        elif type == "delay":
            action_label.config(bg="#F2BB05")
            action_label.config(fg="black")
            action_label.config(text="{0}s Delay".format(self.delay))

        action_label.pack(padx=2, pady=2, ipadx=2, ipady=2)

    def handle_mouse_action(self):
        self.mouse_action_btn.config(relief="sunken")
        self.mouse_action_btn.config(state="disabled")

        self.new_mouse_frame = tk.Frame(self)

        self.header_txt = tk.Label(
            self.new_mouse_frame, text="Press F1 to get mouse position")
        self.header_txt.pack(fill=tk.X, padx=5, pady=5, ipadx=5, ipady=5)

        self.mouse_btn = "left"
        self.mouseleft_btn = tk.Button(
            self.new_mouse_frame, text="Mouse Left", command=lambda: self.set_mouse_button("left"), relief="sunken")
        self.mouseleft_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.mouseright_btn = tk.Button(
            self.new_mouse_frame, text="Mouse Right", command=lambda: self.set_mouse_button("right"))
        self.mouseright_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.position_label = tk.Label(
            self.new_mouse_frame, text="on Position:")
        self.position_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.position_input = tk.Entry(self.new_mouse_frame, width=25)
        self.position_input.insert(0, str(self.mouse_position))
        self.position_input.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.add_click_btn = tk.Button(
            self.new_mouse_frame, text="Add", command=self.add_click, fg="black", bg="#03DD5E")
        self.add_click_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.new_mouse_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def set_mouse_button(self, btn):
        if self.mouse_btn != "left" and btn == "left":
            self.mouseleft_btn.config(relief="sunken")
            self.mouseright_btn.config(relief="raised")
            self.mouse_btn = "left"
        else:
            self.mouseright_btn.config(relief="sunken")
            self.mouseleft_btn.config(relief="raised")
            self.mouse_btn = "right"

    def add_click(self):
        self.mouse_action_btn.config(relief="raised")
        self.mouse_action_btn.config(state="normal")

        self.add_action_info("mouse_click")
        self.exec_order.append(
            (f"mouse_{self.mouse_btn}", self.mouse_position))

        self.new_mouse_frame.destroy()

    def handle_keyboard_action(self):
        self.keyboard_action_btn.config(relief="sunken")
        self.keyboard_action_btn.config(state="disabled")

        self.new_keyboard_frame = tk.Frame(self)

        self.key1_btn = tk.Button(
            self.new_keyboard_frame, text="1", command=lambda: self.set_keyboard_key("1"))
        self.key1_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.key2_btn = tk.Button(
            self.new_keyboard_frame, text="2", command=lambda: self.set_keyboard_key("2"))
        self.key2_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.key3_btn = tk.Button(
            self.new_keyboard_frame, text="3", command=lambda: self.set_keyboard_key("3"))
        self.key3_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.keyspace_btn = tk.Button(
            self.new_keyboard_frame, text="space", command=lambda: self.set_keyboard_key("space"))
        self.keyspace_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.add_keyboard_press_btn = tk.Button(
            self.new_keyboard_frame, text="Add", command=self.add_keyboard_press, fg="black", bg="#03DD5E")
        self.add_keyboard_press_btn.pack(
            side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.new_keyboard_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def set_keyboard_key(self, key):
        if key == "1":
            self.key1_btn.config(relief="sunken")
            self.key2_btn.config(relief="raised")
            self.key3_btn.config(relief="raised")
            self.keyspace_btn.config(relief="raised")
            self.keyboard_key = "1"
        elif key == "2":
            self.key1_btn.config(relief="raised")
            self.key2_btn.config(relief="sunken")
            self.key3_btn.config(relief="raised")
            self.keyspace_btn.config(relief="raised")
            self.keyboard_key = "2"
        elif key == "3":
            self.key1_btn.config(relief="raised")
            self.key2_btn.config(relief="raised")
            self.key3_btn.config(relief="sunken")
            self.keyspace_btn.config(relief="raised")
            self.keyboard_key = "3"
        elif key == "space":
            self.key1_btn.config(relief="raised")
            self.key2_btn.config(relief="raised")
            self.key3_btn.config(relief="raised")
            self.keyspace_btn.config(relief="sunken")
            self.keyboard_key = "space"

    def add_keyboard_press(self):
        self.keyboard_action_btn.config(relief="raised")
        self.keyboard_action_btn.config(state="normal")

        self.add_action_info("keyboard_press")
        self.exec_order.append(
            (f"keyboard_press", self.keyboard_key))

        self.new_keyboard_frame.destroy()

    def handle_new_delay(self):
        self.delay_btn.config(relief="sunken")
        self.delay_btn.config(state="disabled")

        self.new_delay_frame = tk.Frame(self)

        self.delay_label = tk.Label(
            self.new_delay_frame, text="Delay in seconds")
        self.delay_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.delay_input = tk.Entry(self.new_delay_frame, width=10)
        self.delay_input.insert(0, self.delay)
        self.delay_input.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.add_delay_btn = tk.Button(
            self.new_delay_frame, text="Add", command=self.add_delay, fg="black", bg="#03DD5E")
        self.add_delay_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.new_delay_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def add_delay(self):
        self.delay_btn.config(relief="raised")
        self.delay_btn.config(state="normal")

        self.delay = self.delay_input.get()
        self.add_action_info("delay")
        self.exec_order.append(
            (f"delay", self.delay))

        self.new_delay_frame.destroy()
