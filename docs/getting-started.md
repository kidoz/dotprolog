# Your first DotProlog program {#getting-started}

In this tutorial you will run DotProlog from its source repository, then write a program that
greets three people. You will see Prolog find several answers to the same question.

## Prerequisites

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Git. You also need
a text editor and a terminal. Run all commands below from the repository root after cloning it.

This tutorial uses the repository tool so the examples match the checked-out source. Packaged
projects are another entry point; see [create a typed Prolog library](how-to/create-library.md).

## Get the source {#build-and-test}

Get the source:

```console
git clone https://github.com/kidoz/dotprolog.git
cd dotprolog
dotnet --version
```

The last command should print a `10.0` SDK version. The repository's `global.json` selects the SDK;
if .NET reports that it cannot find a compatible version, install the version requested there.

The next command builds the tool automatically. Running the whole test suite is covered in
[build and test the repository](how-to/build-and-test.md).

## Run the Hello World sample

```console
dotnet run --project src/DotProlog.Tool -- run samples/HelloProlog/hello.pl
```

After any build messages, you should see:

```text
Hello! World!
```

Open `samples/HelloProlog/hello.pl`. Its `initialization(main)` directive asks DotProlog to run
`main` after loading the file. The rule prints a greeting and a newline.

## Run your own program

Create a UTF-8 file named `greetings.pl` in the repository root:

```prolog
:- initialization(main).

main :-
    member(Name, [ada, grace, edsger]),
    format('Hello, ~w!~n', [Name]),
    fail.
main.
```

Run it:

```console
dotnet run --project src/DotProlog.Tool -- run greetings.pl
```

You should see these three lines in order:

```text
Hello, ada!
Hello, grace!
Hello, edsger!
```

`member/2` selects a name, and `format/2` prints it. `fail` asks Prolog to try another answer.
After all names have been printed, the final `main.` lets the program finish successfully.

Add `alan` to the list and run the command again. You should get a fourth greeting. You have now
changed a Prolog program and observed how another answer changes its output. You can delete
`greetings.pl` when you are finished.

Continue with [A Gentle Introduction to Prolog](book/index.md) for a sequence of lessons and
exercises, available in English and [Russian](book/ru/index.md).

## Next steps

### Lint source without running it

Follow [lint source locally and in CI](how-to/lint-source.md).

### Run Prolog tests

Follow [run Prolog tests](how-to/run-tests.md).

### Exercise NativeAOT

Follow [publish with NativeAOT](how-to/publish-nativeaot.md). For mode selection and code-list
programs, see [configure language modes and flags](how-to/configure-language.md).
