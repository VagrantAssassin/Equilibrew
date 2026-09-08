kata = str(input("masukan sebuah kata: "))

for i in kata:
    if i != "a" and i != "i" and i != "u" and i != "e" and i != "o":
        print(f"{i}", end="")