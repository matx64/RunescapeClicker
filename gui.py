import tkinter as tk
from pynput.mouse import Button, Controller as MouseController
from pynput.keyboard import Listener, Key, Controller as KeyboardController


class MainApplication(tk.Frame):
    def __init__(self, master=None):
        super().__init__(master)
        self.master = master
        self.mouse_position = None
        self.mouse = MouseController()
        self.pack()
        self.load_header()
        self.master.bind('<KeyPress-F1>', self.on_press)

    def on_press(self, event):
        self.mouse_position = self.mouse.position
        print(self.mouse_position)

    def load_header(self):
        self.header = tk.Frame(self)

        self.mouse_action_btn = tk.Button(
            self.header, text="Add Mouse Click", command=self.handle_mouse_action, fg="white",  bg="blue")
        self.mouse_action_btn.pack(
            side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.keyboard_action_btn = tk.Button(
            self.header, text="Add Keyboard Press", command=self.handle_keyboard_action, fg="white",  bg="blue")
        self.keyboard_action_btn.pack(
            side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.delay_btn = tk.Button(
            self.header, text="Add Delay", command=self.handle_new_delay, fg="black",  bg="yellow")
        self.delay_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.set_stop_btn = tk.Button(
            self.header, text="Set Stop", command=self.handle_new_delay, fg="white",  bg="red")
        self.set_stop_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.header.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def handle_mouse_action(self):
        self.new_action_frame = tk.Frame(self)

        self.header_txt = tk.Label(
            self.new_action_frame, text="Press F1 to get mouse position")
        self.header_txt.pack(fill=tk.X, padx=5, pady=5, ipadx=5, ipady=5)

        self.mouseleft_btn = tk.Button(
            self.new_action_frame, text="Mouse Left", command=self.set_mouse_left)
        self.mouseleft_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.mouseright_btn = tk.Button(
            self.new_action_frame, text="Mouse Right", command=self.set_mouse_right)
        self.mouseright_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.position_label = tk.Label(
            self.new_action_frame, text="on Position:")
        self.position_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.pos_input = tk.Entry(self.new_action_frame, width=25)
        self.pos_input.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

        self.new_action_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)

    def set_mouse_left(self):
        pass

    def set_mouse_right(self):
        pass

    def handle_keyboard_action(self):
        self.new_action_frame = tk.Frame(self)
        self.key1_btn = tk.Button(
            self.new_action_frame, text="1", command=self.set1Key)

    def handle_new_delay(self):
        pass
