import tkinter as tk
from gui import MainApplication


root = tk.Tk()
root.title("Runescape Clicker")
app = MainApplication(master=root)
app.mainloop()
