import tkinter as tk
from pynput.mouse import Controller as MouseController


class MainApplication(tk.Frame):
    from ._mouse import handle_mouse_action, set_mouse_button, add_click
    from ._keyboard import handle_keyboard_action, set_keyboard_key, add_keyboard_press
    from ._delay import handle_new_delay, add_delay
    from ._stop import handle_stop, add_stop

    def __init__(self, master=None):
        super().__init__(master)
        self.master = master
        self.mouse = MouseController()
        self.mouse_position = (100, 100)
        self.mouse_btn = "left"
        self.keyboard_key = "space"
        self.delay = 0
        self.exec_order = []
        self.stop_option = 1
        self.stop_time = 0
        self.pack()
        self.load_header()
        self.load_info_frame()
        self.position_input = tk.Entry(self)
        self.position_input.destroy()
        self.master.bind('<KeyPress-F1>', self.on_press)

    def on_press(self, event):
        if self.position_input.winfo_exists():
            self.mouse_position = self.mouse.position
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
            action_label.config(text="{0}s Delay".format(self.delay))

        action_label.pack(padx=2, pady=2, ipadx=2, ipady=2)

    def start(self):
        pass
