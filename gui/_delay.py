import tkinter as tk


def handle_new_delay(self):
    self.delay_btn.config(relief="sunken")
    self.delay_btn.config(state="disabled")

    self.new_delay_frame = tk.Frame(self)

    delay_label = tk.Label(
        self.new_delay_frame, text="Delay in seconds")
    delay_label.pack(side="left", padx=5, pady=5, ipadx=5, ipady=5)

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
