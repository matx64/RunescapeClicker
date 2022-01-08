import tkinter as tk


def handle_stop(self):
    self.set_stop_btn.config(relief="sunken")
    self.set_stop_btn.config(state="disabled")

    self.new_stop_frame = tk.Frame(self)
    option1_frame = tk.Frame(self.new_stop_frame)
    option2_frame = tk.Frame(self.new_stop_frame)

    option1_label = tk.Label(option1_frame, text="Stop on F2 press")
    option1_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

    option1_btn = tk.Button(
        option1_frame, text="Save", command=lambda: self.add_stop(1), fg="black", bg="#03DD5E")
    option1_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

    option2_label = tk.Label(option2_frame, text="Stop after")
    option2_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

    self.stop_input = tk.Entry(option2_frame, width=10)
    self.stop_input.insert(0, self.stop_time)
    self.stop_input.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

    option2_label = tk.Label(option2_frame, text="seconds")
    option2_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

    option2_btn = tk.Button(
        option2_frame, text="Save", command=lambda: self.add_stop(2), fg="black", bg="#03DD5E")
    option2_btn.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

    option1_frame.pack()
    option2_frame.pack()
    self.new_stop_frame.pack(padx=5, pady=5, ipadx=5, ipady=5)


def add_stop(self, option):
    self.set_stop_btn.config(relief="raised")
    self.set_stop_btn.config(state="normal")
    self.stop_info.config(fg="white")
    self.stop_info.config(bg="#F9564F")

    self.stop_option = option

    if option == 1:
        self.stop_time = 0

        self.stop_info.config(text="Stop on F2 press")
    else:
        self.stop_time = self.stop_input.get()
        self.stop_info.config(
            text=f"Stop after {self.stop_time} seconds OR F2 Press")

    if self.start_btn.winfo_exists():
        self.start_btn.destroy()

    self.start_btn = tk.Button(
        self.info_footer, text="START", command=self.before_start, fg="black", bg="#03DD5E")
    self.start_btn.pack(padx=5, pady=5, ipadx=5, ipady=5)

    self.new_stop_frame.destroy()
