import tkinter as tk


def handle_mouse_action(self):
    self.mouse_action_btn.config(relief="sunken")
    self.mouse_action_btn.config(state="disabled")

    self.new_mouse_frame = tk.Frame(self)

    header_txt = tk.Label(
        self.new_mouse_frame, text="Press F1 to get mouse position")
    header_txt.pack(fill=tk.X, padx=5, pady=5, ipadx=5, ipady=5)

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
        (f"click_mouse_{self.mouse_btn}", self.mouse_position))

    self.new_mouse_frame.destroy()
