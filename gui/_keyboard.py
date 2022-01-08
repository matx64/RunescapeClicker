import tkinter as tk


def handle_keyboard_action(self):
    self.keyboard_action_btn.config(relief="sunken")
    self.keyboard_action_btn.config(state="disabled")

    self.new_keyboard_frame = tk.Frame(self)

    header_txt = tk.Label(
        self.new_keyboard_frame, text="Select a keyboard key")
    header_txt.pack(fill=tk.X)

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
        ("press_key", self.keyboard_key))

    self.new_keyboard_frame.destroy()
