# StorSphere - A Basic Tar-Like File Format in C#

StorSphere is a C# program that implements a basic tar-like file format with a command-line interface (CLI) for packing and unpacking files. This project is intended for educational purposes and serves as a minimal example.

## Usage

### Pack Files

To pack files into a StorSphere Archive:

```bash
storsphere pack myarchive.ssph file1.txt file2.png
```

### Unpack Files

To unpack files from a StorSphere Archive:

```
storsphere unpack myarchive.ssph output_folder
```

### List Contents

To list the contents of a StorSphere Archive:

```
storsphere list myarchive.ssph
```

### Code Explanation

The code consists of a simple C# console application with two main functionalities:

1. Packing Files:

    - The `Pack` method takes a list of file paths and creates a StorSphere Archive.
    - It uses GZip compression and stores file names along with their content sizes.

2. Unpacking Files:

    - The `Unpack` method extracts files from the StorSphere Archive to a specified output folder.
    - It reads the archive, recreates the files, and stores them in the output folder.

3. Listing Contents:

    - The ListContents method displays the names and sizes of files within the StorSphere Archive.

### Requirements

- .NET Core SDK

### How to Run

1. Clone this repository
```bash
git clone https://github.com/CraftMusic-App-Studios/StorSphere
cd StorSphere
```
2. Run the program
```bash
storsphere pack output.ssph file1.txt file2.png
```
or 
```bash
storsphere unpack input.ssph output_folder
```