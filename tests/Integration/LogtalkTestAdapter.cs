using System.Text;

namespace Integration.Tests;

/// <summary>
/// Reads ISO-prefixed lgtunit declarations while treating their goals and expectations as opaque
/// Prolog text. It adapts the wrapper, never the expected behavior.
/// </summary>
internal static class LogtalkTestAdapter
{
    private const string TextInputAssertionSupport = """
        '$logtalk_text_input_assertion'(Expected, Assertion) :-
            current_input(Stream),
            atom_length(Expected, Length),
            Limit is Length + 1,
            '$logtalk_read_text_input'(Stream, Chars, Limit),
            atom_chars(Contents, Chars),
            Assertion = (Expected == Contents).

        '$logtalk_text_input_assertion'(Alias, Expected, Assertion) :-
            atom_length(Expected, Length),
            Limit is Length + 1,
            '$logtalk_read_text_input'(Alias, Chars, Limit),
            atom_chars(Contents, Chars),
            close(Alias),
            '$logtalk_delete_text_input'(Alias),
            Assertion = (Expected == Contents).

        '$logtalk_read_text_input'(Stream, Chars, Countdown) :-
            get_char(Stream, Char),
            ( Char == end_of_file ->
                Chars = []
            ; Countdown =< 0 ->
                Chars = []
            ; Chars = [Char| Rest],
              Next is Countdown - 1,
              '$logtalk_read_text_input'(Stream, Rest, Next)
            ).
        """;

    private const string BinaryOutputSupport = """
        '$logtalk_set_binary_output'(Bytes) :-
            '$logtalk_create_binary_output'(Bytes, Stream),
            set_output(Stream).
        """;

    /// <summary>Reads every enabled and explicitly disabled <c>iso_*</c> declaration in one source.</summary>
    internal static IReadOnlyList<LogtalkTestDeclaration> ReadDeclarations(string source, string relativePath)
    {
        var declarations = new List<LogtalkTestDeclaration>();
        var conditionals = new List<ConditionalFrame>();

        foreach (string clause in SplitClauses(source))
        {
            string text = TrimLeadingTrivia(clause);
            if (TryReadDirectiveArgument(text, "if", out string condition))
            {
                conditionals.Add(new ConditionalFrame(condition));
                continue;
            }

            if (TryReadDirectiveArgument(text, "elif", out condition))
            {
                if (conditionals.Count == 0)
                {
                    throw new InvalidDataException($"{relativePath}: elif directive has no matching if directive.");
                }

                conditionals[^1].BeginAlternative(condition);
                continue;
            }

            if (text == ":- else.")
            {
                if (conditionals.Count == 0)
                {
                    throw new InvalidDataException($"{relativePath}: else directive has no matching if directive.");
                }

                conditionals[^1].BeginElse();
                continue;
            }

            if (text == ":- endif.")
            {
                if (conditionals.Count == 0)
                {
                    throw new InvalidDataException($"{relativePath}: endif directive has no matching if directive.");
                }

                conditionals.RemoveAt(conditionals.Count - 1);
                continue;
            }

            bool quickCheckDisabled = text.StartsWith("- quick_check(iso", StringComparison.Ordinal);
            int quickCheckStart =
                quickCheckDisabled ? 2
                : text.StartsWith("quick_check(iso", StringComparison.Ordinal) ? 0
                : -1;
            if (quickCheckStart >= 0)
            {
                string quickCheckHead = text[quickCheckStart..].Trim();
                if (!quickCheckHead.EndsWith('.'))
                {
                    throw new InvalidDataException(
                        $"{relativePath}: quick-check declaration has no full stop: {quickCheckHead}"
                    );
                }

                quickCheckHead = quickCheckHead[..^1].Trim();
                if (!quickCheckHead.StartsWith("quick_check(", StringComparison.Ordinal) || !quickCheckHead.EndsWith(')'))
                {
                    throw new InvalidDataException($"{relativePath}: malformed quick-check declaration: {quickCheckHead}");
                }

                List<string> quickCheckArguments = SplitTopLevel(quickCheckHead[12..^1], ',');
                if (
                    quickCheckArguments.Count != 2
                    || !quickCheckArguments[0].StartsWith("iso_", StringComparison.Ordinal)
                )
                {
                    throw new InvalidDataException(
                        $"{relativePath}: malformed ISO quick-check declaration: {quickCheckHead}"
                    );
                }

                declarations.Add(
                    new LogtalkTestDeclaration(
                        relativePath,
                        quickCheckArguments[0],
                        $"quick_check({quickCheckArguments[1]})",
                        null,
                        null,
                        quickCheckDisabled,
                        ConditionalGoal(conditionals)
                    )
                );
                continue;
            }

            bool disabled = text.StartsWith("- test(iso", StringComparison.Ordinal);
            int testStart =
                disabled ? 2
                : text.StartsWith("test(iso", StringComparison.Ordinal) ? 0
                : -1;

            if (testStart < 0)
            {
                continue;
            }

            string declaration = text[testStart..];
            int neck = FindTopLevel(declaration, ":-");
            if (neck < 0)
            {
                throw new InvalidDataException($"{relativePath}: test declaration has no clause body: {declaration}");
            }

            string head = declaration[..neck].Trim();
            if (!head.StartsWith("test(", StringComparison.Ordinal) || !head.EndsWith(')'))
            {
                throw new InvalidDataException($"{relativePath}: malformed test head: {head}");
            }

            List<string> arguments = SplitTopLevel(head[5..^1], ',');
            if (arguments.Count is < 1 or > 3)
            {
                throw new InvalidDataException(
                    $"{relativePath}: expected one to three test arguments but found {arguments.Count}: {head}"
                );
            }

            string id = arguments[0];
            if (!id.StartsWith("iso_", StringComparison.Ordinal))
            {
                continue;
            }

            string body = declaration[(neck + 2)..].Trim();
            if (!body.EndsWith('.'))
            {
                throw new InvalidDataException($"{relativePath}: test body has no terminating full stop: {id}");
            }

            body = body[..^1].Trim();
            if (body.Length == 0)
            {
                throw new InvalidDataException($"{relativePath}: test body is empty: {id}");
            }

            declarations.Add(
                new LogtalkTestDeclaration(
                    relativePath,
                    id,
                    arguments.Count >= 2 ? arguments[1] : "true",
                    arguments.Count == 3 ? arguments[2] : null,
                    body,
                    disabled,
                    ConditionalGoal(conditionals)
                )
            );
        }

        if (conditionals.Count != 0)
        {
            throw new InvalidDataException($"{relativePath}: conditional directive has no matching endif directive.");
        }

        return declarations;
    }

    /// <summary>
    /// Extracts unconditional source-local Prolog helpers while rejecting Logtalk-specific or
    /// backend-conditional support code that cannot yet be translated mechanically.
    /// </summary>
    internal static bool TryReadSupportProgram(string source, out string program)
    {
        var support = new List<string>();
        int conditionalDepth = 0;

        foreach (string clause in SplitClauses(source))
        {
            string text = TrimLeadingTrivia(clause);
            if (text.Length == 0)
            {
                continue;
            }

            if (
                text.StartsWith("test(", StringComparison.Ordinal)
                || text.StartsWith("- test(", StringComparison.Ordinal)
                || text.StartsWith("quick_check(", StringComparison.Ordinal)
                || text.StartsWith("- quick_check(", StringComparison.Ordinal)
                || text.StartsWith(":- object", StringComparison.Ordinal)
                || text.StartsWith(":- info", StringComparison.Ordinal)
                || text.StartsWith(":- end_object", StringComparison.Ordinal)
                || text.StartsWith(":- public", StringComparison.Ordinal)
                || text.StartsWith(":- private", StringComparison.Ordinal)
                || text.StartsWith(":- protected", StringComparison.Ordinal)
                || text.StartsWith(":- uses", StringComparison.Ordinal)
                || text.StartsWith(":- meta_predicate", StringComparison.Ordinal)
            )
            {
                continue;
            }

            // lgtunit invokes these object hooks around tests. A hook implemented through Logtalk
            // message dispatch is wrapper infrastructure, not a source-local Prolog helper.
            if (IsDispatchedLifecycleHook(text))
            {
                continue;
            }

            if (text.StartsWith(":- if", StringComparison.Ordinal))
            {
                conditionalDepth++;
                continue;
            }

            if (text.StartsWith(":- elif", StringComparison.Ordinal) || text.StartsWith(":- else", StringComparison.Ordinal))
            {
                continue;
            }

            if (text.StartsWith(":- endif", StringComparison.Ordinal))
            {
                conditionalDepth--;
                if (conditionalDepth < 0)
                {
                    program = string.Empty;
                    return false;
                }

                continue;
            }

            // These declarations only describe how a Prolog system may lay out source predicates.
            if (
                text.StartsWith(":- multifile", StringComparison.Ordinal)
                || text.StartsWith(":- discontiguous", StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (text.StartsWith(":- dynamic", StringComparison.Ordinal))
            {
                if (conditionalDepth > 0)
                {
                    program = string.Empty;
                    return false;
                }

                support.Add(text);
                continue;
            }

            if (TryReadDirectiveArgument(text, "op", out _))
            {
                if (conditionalDepth > 0)
                {
                    program = string.Empty;
                    return false;
                }

                support.Add(text);
                continue;
            }

            // OS-dispatched helpers in the pinned sources serve portability tests outside the ISO
            // inventory. They cannot be replayed as plain Prolog and are never called by an adapted
            // ISO declaration.
            if (text.Contains("os::", StringComparison.Ordinal))
            {
                continue;
            }

            if (
                conditionalDepth > 0
                || text.StartsWith(":-", StringComparison.Ordinal)
                || text.Contains("^^", StringComparison.Ordinal)
                || text.Contains("::", StringComparison.Ordinal)
                || !TryTranslateSupportClause(text, out string translated)
            )
            {
                program = string.Empty;
                return false;
            }

            support.Add(translated);
        }

        if (conditionalDepth != 0)
        {
            program = string.Empty;
            return false;
        }

        if (source.Contains("^^text_input_assertion(", StringComparison.Ordinal))
        {
            support.Add(TextInputAssertionSupport);
        }

        if (source.Contains("^^set_binary_output(", StringComparison.Ordinal))
        {
            support.Add(BinaryOutputSupport);
        }

        program = string.Join(Environment.NewLine, support);
        return true;
    }

    /// <summary>
    /// Unwraps one or more conjoined Logtalk backend escapes, leaving each Prolog goal unchanged.
    /// </summary>
    internal static bool TryUnwrapBackendGoal(LogtalkTestDeclaration declaration, out string goal)
    {
        if (declaration.Body is null)
        {
            goal = string.Empty;
            return false;
        }

        return TryUnwrapBackendBody(declaration.Body, out goal)
            || TryUnwrapFindallBackendGoal(declaration.Body, out goal)
            || TryTranslateEmbeddedBackendGoal(declaration.Body, out goal);
    }

    private static bool TryUnwrapFindallBackendGoal(string source, out string goal)
    {
        string body = source.Trim();
        const string prefix = "findall(";
        if (
            !body.StartsWith(prefix, StringComparison.Ordinal)
            || !body.EndsWith(')')
            || !OuterParenthesesEndAt(body, prefix.Length - 1)
        )
        {
            goal = string.Empty;
            return false;
        }

        List<string> arguments = SplitTopLevel(body[prefix.Length..^1], ',');
        if (arguments.Count != 3 || !TryUnwrapBackendBody(arguments[1], out string generator))
        {
            goal = string.Empty;
            return false;
        }

        goal = $"findall({arguments[0]}, ({generator}), {arguments[2]})";
        return true;
    }

    private static bool OuterParenthesesEndAt(string source, int opening)
    {
        var state = new ScanState();

        for (int index = opening; index < source.Length; index++)
        {
            Advance(source, ref index, state);
            if (index > opening && state.Parentheses == 0)
            {
                return index == source.Length - 1;
            }
        }

        return false;
    }

    private static bool TryTranslateEmbeddedBackendGoal(string source, out string goal)
    {
        if (
            source.Contains("::", StringComparison.Ordinal)
            || !TryTranslateLgtunitHelpers(source, out string helpersTranslated, out bool foundHelper)
        )
        {
            goal = string.Empty;
            return false;
        }

        char[] translated = helpersTranslated.ToCharArray();
        var state = new ScanState();
        bool foundBackendEscape = false;

        for (int index = 0; index < helpersTranslated.Length; index++)
        {
            bool shielded = state.LineComment || state.BlockComment || state.Quote != '\0';
            int current = index;
            Advance(helpersTranslated, ref index, state);

            if (shielded || current != index)
            {
                continue;
            }

            if (helpersTranslated[current] == '{')
            {
                translated[current] = '(';
                foundBackendEscape = true;
            }
            else if (helpersTranslated[current] == '}')
            {
                translated[current] = ')';
            }
        }

        goal = (foundBackendEscape || foundHelper) && state.Braces == 0 ? new string(translated) : string.Empty;
        return goal.Length > 0;
    }

    private static bool TryTranslateLgtunitHelpers(string source, out string translated, out bool found)
    {
        var result = new StringBuilder(source.Length);
        var state = new ScanState();
        int copyStart = 0;
        found = false;

        for (int index = 0; index < source.Length; index++)
        {
            bool shielded = state.LineComment || state.BlockComment || state.Quote != '\0';
            int current = index;
            Advance(source, ref index, state);

            if (shielded || current != index)
            {
                continue;
            }

            const string suppressTextOutput = "^^suppress_text_output";
            if (
                source.AsSpan(current).StartsWith(suppressTextOutput, StringComparison.Ordinal)
                && (
                    current + suppressTextOutput.Length == source.Length
                    || !(
                        char.IsLetterOrDigit(source[current + suppressTextOutput.Length])
                        || source[current + suppressTextOutput.Length] == '_'
                    )
                )
            )
            {
                result.Append(source, copyStart, current - copyStart);
                result.Append("'$logtalk_suppress_text_output'");
                copyStart = current + suppressTextOutput.Length;
                index = copyStart - 1;
                found = true;
                continue;
            }

            int functorStart = current;
            bool dispatchedAssertion = source.AsSpan(current).StartsWith("^^assertion(", StringComparison.Ordinal);
            bool dispatchedTextInput = source
                .AsSpan(current)
                .StartsWith("^^set_text_input(", StringComparison.Ordinal);
            bool dispatchedTextOutput = source
                .AsSpan(current)
                .StartsWith("^^set_text_output(", StringComparison.Ordinal);
            bool dispatchedTextOutputAssertion = source
                .AsSpan(current)
                .StartsWith("^^text_output_assertion(", StringComparison.Ordinal);
            bool dispatchedTextOutputContents = source
                .AsSpan(current)
                .StartsWith("^^text_output_contents(", StringComparison.Ordinal);
            bool dispatchedTextInputAssertion = source
                .AsSpan(current)
                .StartsWith("^^text_input_assertion(", StringComparison.Ordinal);
            bool dispatchedCheckTextOutput = source
                .AsSpan(current)
                .StartsWith("^^check_text_output(", StringComparison.Ordinal);
            bool dispatchedFilePath = source
                .AsSpan(current)
                .StartsWith("^^file_path(", StringComparison.Ordinal);
            bool dispatchedCreateTextFile = source
                .AsSpan(current)
                .StartsWith("^^create_text_file(", StringComparison.Ordinal);
            bool dispatchedCreateBinaryFile = source
                .AsSpan(current)
                .StartsWith("^^create_binary_file(", StringComparison.Ordinal);
            bool dispatchedClosedInputStream = source
                .AsSpan(current)
                .StartsWith("^^closed_input_stream(", StringComparison.Ordinal);
            bool dispatchedClosedOutputStream = source
                .AsSpan(current)
                .StartsWith("^^closed_output_stream(", StringComparison.Ordinal);
            bool dispatchedSetBinaryOutput = source
                .AsSpan(current)
                .StartsWith("^^set_binary_output(", StringComparison.Ordinal);
            bool dispatchedBinaryOutputAssertion = source
                .AsSpan(current)
                .StartsWith("^^binary_output_assertion(", StringComparison.Ordinal);
            if (
                source.AsSpan(current).StartsWith("^^", StringComparison.Ordinal)
                && !dispatchedAssertion
                && !dispatchedTextInput
                && !dispatchedTextOutput
                && !dispatchedTextOutputAssertion
                && !dispatchedTextOutputContents
                && !dispatchedTextInputAssertion
                && !dispatchedCheckTextOutput
                && !dispatchedFilePath
                && !dispatchedCreateTextFile
                && !dispatchedCreateBinaryFile
                && !dispatchedClosedInputStream
                && !dispatchedClosedOutputStream
                && !dispatchedSetBinaryOutput
                && !dispatchedBinaryOutputAssertion
            )
            {
                translated = string.Empty;
                return false;
            }

            if (
                dispatchedAssertion
                || dispatchedTextInput
                || dispatchedTextOutput
                || dispatchedTextOutputAssertion
                || dispatchedTextOutputContents
                || dispatchedTextInputAssertion
                || dispatchedCheckTextOutput
                || dispatchedFilePath
                || dispatchedCreateTextFile
                || dispatchedCreateBinaryFile
                || dispatchedClosedInputStream
                || dispatchedClosedOutputStream
                || dispatchedSetBinaryOutput
                || dispatchedBinaryOutputAssertion
            )
            {
                functorStart += 2;
            }

            string? functor =
                IsFunctorCallAt(source, functorStart, "assertion") ? "assertion"
                : IsFunctorCallAt(source, functorStart, "variant") ? "variant"
                : IsFunctorCallAt(source, functorStart, "set_text_input") ? "set_text_input"
                : IsFunctorCallAt(source, functorStart, "set_text_output") ? "set_text_output"
                : IsFunctorCallAt(source, functorStart, "text_output_assertion") ? "text_output_assertion"
                : IsFunctorCallAt(source, functorStart, "text_output_contents") ? "text_output_contents"
                : IsFunctorCallAt(source, functorStart, "text_input_assertion") ? "text_input_assertion"
                : IsFunctorCallAt(source, functorStart, "check_text_output") ? "check_text_output"
                : IsFunctorCallAt(source, functorStart, "file_path") ? "file_path"
                : IsFunctorCallAt(source, functorStart, "create_text_file") ? "create_text_file"
                : IsFunctorCallAt(source, functorStart, "create_binary_file") ? "create_binary_file"
                : IsFunctorCallAt(source, functorStart, "closed_input_stream") ? "closed_input_stream"
                : IsFunctorCallAt(source, functorStart, "closed_output_stream") ? "closed_output_stream"
                : IsFunctorCallAt(source, functorStart, "set_binary_output") ? "set_binary_output"
                : IsFunctorCallAt(source, functorStart, "binary_output_assertion") ? "binary_output_assertion"
                : null;
            if (functor is null)
            {
                continue;
            }

            int opening = functorStart + functor.Length;
            int closing = FindMatchingParenthesis(source, opening);
            if (closing < 0)
            {
                translated = string.Empty;
                return false;
            }

            string arguments = source[(opening + 1)..closing];
            string replacement;
            if (functor == "assertion")
            {
                if (!TryTranslateLgtunitHelpers(arguments, out string assertion, out _))
                {
                    translated = string.Empty;
                    return false;
                }

                replacement = $"({assertion})";
            }
            else if (functor == "variant")
            {
                List<string> variantArguments = SplitTopLevel(arguments, ',');
                if (variantArguments.Count != 2)
                {
                    translated = string.Empty;
                    return false;
                }

                string left = variantArguments[0];
                string right = variantArguments[1];
                replacement =
                    $"(subsumes_term(({left}), ({right})), subsumes_term(({right}), ({left})))";
            }
            else
            {
                List<string> hostArguments = SplitTopLevel(arguments, ',');
                bool supportedArity = functor switch
                {
                    "set_text_input" => hostArguments.Count is 1 or 2,
                    "set_text_output" or "text_output_contents" => hostArguments.Count is 1 or 2,
                    "text_output_assertion" => hostArguments.Count is 2 or 3,
                    "text_input_assertion" => hostArguments.Count is 2 or 3,
                    "set_binary_output" => hostArguments.Count is 1 or 2,
                    "binary_output_assertion" => hostArguments.Count is 2 or 3,
                    _ => hostArguments.Count == 2,
                };
                if (!supportedArity)
                {
                    translated = string.Empty;
                    return false;
                }

                string hostFunctor = functor switch
                {
                    "set_text_input" => "$logtalk_set_text_input",
                    "set_text_output" => "$logtalk_set_text_output",
                    "text_output_assertion" => "$logtalk_text_output_assertion",
                    "text_output_contents" => "$logtalk_text_output_contents",
                    "text_input_assertion" => "$logtalk_text_input_assertion",
                    "check_text_output" => "$logtalk_check_text_output",
                    "file_path" => "$logtalk_file_path",
                    "create_text_file" => "$logtalk_create_text_file",
                    "create_binary_file" => "$logtalk_create_binary_file",
                    "closed_input_stream" => "$logtalk_closed_input_stream",
                    "closed_output_stream" => "$logtalk_closed_output_stream",
                    "set_binary_output" when hostArguments.Count == 1 => "$logtalk_set_binary_output",
                    "set_binary_output" => "$logtalk_set_named_binary_output",
                    _ => "$logtalk_binary_output_assertion",
                };
                replacement = $"'{hostFunctor}'({arguments})";
            }

            result.Append(source, copyStart, current - copyStart);
            result.Append(replacement);
            copyStart = closing + 1;
            index = closing;
            found = true;
        }

        result.Append(source, copyStart, source.Length - copyStart);
        translated = result.ToString();
        return true;
    }

    private static bool IsFunctorCallAt(string source, int start, string functor)
    {
        if (
            start < 0
            || !source.AsSpan(start).StartsWith(functor, StringComparison.Ordinal)
            || start + functor.Length >= source.Length
            || source[start + functor.Length] != '('
        )
        {
            return false;
        }

        return start == 0 || !(char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_');
    }

    private static int FindMatchingParenthesis(string source, int opening)
    {
        var state = new ScanState();

        for (int index = opening; index < source.Length; index++)
        {
            Advance(source, ref index, state);
            if (index > opening && state.Parentheses == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryTranslateSupportClause(string clause, out string translated)
    {
        int neck = FindTopLevel(clause, ":-");
        if (neck < 0)
        {
            if (clause.Contains('{') || clause.Contains('}'))
            {
                translated = string.Empty;
                return false;
            }

            translated = clause;
            return true;
        }

        string body = clause[(neck + 2)..].Trim();
        if (!body.EndsWith('.'))
        {
            translated = string.Empty;
            return false;
        }

        body = body[..^1].Trim();
        if (!body.Contains('{') && !body.Contains('}'))
        {
            translated = clause;
            return true;
        }

        if (!TryUnwrapBackendBody(body, out string backendGoal))
        {
            translated = string.Empty;
            return false;
        }

        translated = $"{clause[..(neck + 2)]}{Environment.NewLine}    {backendGoal}.";
        return true;
    }

    private static bool IsDispatchedLifecycleHook(string clause)
    {
        int neck = FindTopLevel(clause, ":-");
        if (neck < 0)
        {
            return false;
        }

        string head = clause[..neck].Trim();
        return head is "setup" or "cleanup"
            && (
                clause.Contains("^^", StringComparison.Ordinal)
                || clause.Contains("::", StringComparison.Ordinal)
            );
    }

    private static bool TryUnwrapBackendBody(string source, out string goal)
    {
        string body = source.Trim();
        List<string> parts = SplitTopLevel(body, ',');
        var goals = new List<string>(parts.Count);

        foreach (string part in parts)
        {
            string wrapper = part.Trim();
            if (wrapper.Length < 3 || wrapper[0] != '{' || wrapper[^1] != '}')
            {
                goal = string.Empty;
                return false;
            }

            string backendGoal = wrapper[1..^1].Trim();
            if (backendGoal.Length == 0)
            {
                goal = string.Empty;
                return false;
            }

            goals.Add($"({backendGoal})");
        }

        goal = string.Join(", ", goals);
        return goals.Count > 0;
    }

    /// <summary>
    /// Translates lgtunit's numeric approximate-equality assertion to its pinned implementation.
    /// Other assertions remain unchanged.
    /// </summary>
    internal static string TranslateAssertion(string assertion)
    {
        if (TryUnwrapBackendBody(assertion, out string backendAssertion))
        {
            assertion = backendAssertion;
        }

        int approximateEquality = FindTopLevel(assertion, "=~=");
        if (approximateEquality < 0)
        {
            return assertion;
        }

        string left = assertion[..approximateEquality].Trim();
        string right = assertion[(approximateEquality + 3)..].Trim();
        if (left.Length == 0 || right.Length == 0 || FindTopLevel(right, "=~=") >= 0)
        {
            throw new InvalidDataException($"Malformed lgtunit approximate-equality assertion: {assertion}");
        }

        string difference = $"abs(({left}) - ({right}))";
        return $"(({difference} < 0.0000000001) -> true ; ({difference} < (0.00001 * max(abs({left}), abs({right})))))";
    }

    /// <summary>
    /// Translates Logtalk's backend escape braces inside a conditional directive to ordinary Prolog
    /// grouping. The pinned corpus uses these braces only to ask the backend about a capability.
    /// </summary>
    internal static string TranslateConditionalGoal(string condition) =>
        condition
            .Replace('{', '(')
            .Replace('}', ')')
            .Replace(
                "os::operating_system_type(windows)",
                "'$logtalk_is_windows'",
                StringComparison.Ordinal
            );

    /// <summary>Wraps each accepted lgtunit error term in the ISO <c>error/2</c> ball shape.</summary>
    internal static string TranslateErrorAlternatives(string errors)
    {
        string list = errors.Trim();
        if (list.Length < 2 || list[0] != '[' || list[^1] != ']')
        {
            throw new InvalidDataException($"Malformed lgtunit errors expectation: {errors}");
        }

        List<string> alternatives = SplitTopLevel(list[1..^1], ',');
        if (alternatives.Count == 0 || alternatives.Any(alternative => alternative.Length == 0))
        {
            throw new InvalidDataException($"Malformed lgtunit errors expectation: {errors}");
        }

        return $"[{string.Join(", ", alternatives.Select(alternative => $"error(({alternative}), _)"))}]";
    }

    /// <summary>Finds the comma separating two opaque expectation arguments.</summary>
    internal static int FindArgumentSeparator(string arguments) => FindTopLevel(arguments, ",");

    private static List<string> SplitClauses(string source)
    {
        var clauses = new List<string>();
        int start = 0;
        var state = new ScanState();

        for (int index = 0; index < source.Length; index++)
        {
            if (Advance(source, ref index, state))
            {
                continue;
            }

            if (source[index] == '.' && state.IsTopLevel && IsLayoutOrEndAfterFullStop(source, index + 1))
            {
                clauses.Add(source[start..(index + 1)]);
                start = index + 1;
            }
        }

        return clauses;
    }

    private static bool TryReadDirectiveArgument(string directive, string name, out string argument)
    {
        string prefix = $":- {name}(";
        if (!directive.StartsWith(prefix, StringComparison.Ordinal) || !directive.EndsWith(").", StringComparison.Ordinal))
        {
            argument = string.Empty;
            return false;
        }

        argument = directive[prefix.Length..^2].Trim();
        return argument.Length > 0;
    }

    private static string? ConditionalGoal(List<ConditionalFrame> conditionals)
    {
        if (conditionals.Count == 0)
        {
            return null;
        }

        return conditionals.Count == 1
            ? conditionals[0].Goal
            : string.Join(", ", conditionals.Select(frame => $"({frame.Goal})"));
    }

    private static int FindTopLevel(string source, string token)
    {
        var state = new ScanState();

        for (int index = 0; index <= source.Length - token.Length; index++)
        {
            if (Advance(source, ref index, state))
            {
                continue;
            }

            if (state.IsTopLevel && source.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> SplitTopLevel(string source, char separator)
    {
        var parts = new List<string>();
        int start = 0;
        var state = new ScanState();

        for (int index = 0; index < source.Length; index++)
        {
            if (Advance(source, ref index, state))
            {
                continue;
            }

            if (source[index] == separator && state.IsTopLevel)
            {
                parts.Add(source[start..index].Trim());
                start = index + 1;
            }
        }

        parts.Add(source[start..].Trim());
        return parts;
    }

    /// <summary>
    /// Advances quote, comment, and delimiter state. Returns true when the current character belongs
    /// to lexical shielding and therefore cannot be structural punctuation.
    /// </summary>
    private static bool Advance(string source, ref int index, ScanState state)
    {
        char current = source[index];
        char next = index + 1 < source.Length ? source[index + 1] : '\0';

        if (state.LineComment)
        {
            if (current == '\n')
            {
                state.LineComment = false;
            }

            return true;
        }

        if (state.BlockComment)
        {
            if (current == '*' && next == '/')
            {
                state.BlockComment = false;
                index++;
            }

            return true;
        }

        if (state.Quote != '\0')
        {
            if (state.Escaped)
            {
                state.Escaped = false;
            }
            else if (current == '\\')
            {
                state.Escaped = true;
            }
            else if (current == state.Quote)
            {
                if (next == state.Quote)
                {
                    index++;
                }
                else
                {
                    state.Quote = '\0';
                }
            }

            return true;
        }

        if (current == '%')
        {
            state.LineComment = true;
            return true;
        }

        if (current == '/' && next == '*')
        {
            state.BlockComment = true;
            index++;
            return true;
        }

        // In 0'c the apostrophe introduces a character payload; it is not an opening quote.
        if (current == '\'' && index > 0 && source[index - 1] == '0')
        {
            SkipCharacterCodePayload(source, ref index);
            return true;
        }

        if (current is '\'' or '"' or '`')
        {
            state.Quote = current;
            return true;
        }

        switch (current)
        {
            case '(':
                state.Parentheses++;
                return true;
            case ')':
                state.Parentheses--;
                return true;
            case '[':
                state.Brackets++;
                return true;
            case ']':
                state.Brackets--;
                return true;
            case '{':
                state.Braces++;
                return true;
            case '}':
                state.Braces--;
                return true;
            default:
                return false;
        }
    }

    private static void SkipCharacterCodePayload(string source, ref int apostrophe)
    {
        int payload = apostrophe + 1;
        if (payload >= source.Length)
        {
            return;
        }

        if (source[payload] != '\\')
        {
            apostrophe = payload;
            return;
        }

        int escaped = payload + 1;
        if (escaped >= source.Length)
        {
            apostrophe = payload;
            return;
        }

        if (source[escaped] is 'x' or 'o')
        {
            int closing = source.IndexOf('\\', escaped + 1);
            apostrophe = closing < 0 ? escaped : closing;
            return;
        }

        apostrophe = escaped;
    }

    private static bool IsLayoutOrEndAfterFullStop(string source, int index) =>
        index >= source.Length
        || char.IsWhiteSpace(source[index])
        || source[index] == '%'
        || (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*');

    private static string TrimLeadingTrivia(string source)
    {
        int index = 0;

        while (index < source.Length)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index < source.Length && source[index] == '%')
            {
                int newline = source.IndexOf('\n', index + 1);
                index = newline < 0 ? source.Length : newline + 1;
                continue;
            }

            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
            {
                int closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = closing < 0 ? source.Length : closing + 2;
                continue;
            }

            break;
        }

        return source[index..].Trim();
    }

    private sealed class ScanState
    {
        internal int Parentheses { get; set; }

        internal int Brackets { get; set; }

        internal int Braces { get; set; }

        internal char Quote { get; set; }

        internal bool Escaped { get; set; }

        internal bool LineComment { get; set; }

        internal bool BlockComment { get; set; }

        internal bool IsTopLevel => Parentheses == 0 && Brackets == 0 && Braces == 0;
    }

    private sealed class ConditionalFrame(string firstCondition)
    {
        private readonly List<string> _previous = [];
        private string? _current = firstCondition;

        internal string Goal
        {
            get
            {
                if (_current is null)
                {
                    return $"\\+ ({string.Join(" ; ", _previous)})";
                }

                return _previous.Count == 0
                    ? _current
                    : $"\\+ ({string.Join(" ; ", _previous)}), ({_current})";
            }
        }

        internal void BeginAlternative(string condition)
        {
            if (_current is null)
            {
                throw new InvalidDataException("An elif directive cannot follow an else directive.");
            }

            _previous.Add(_current);
            _current = condition;
        }

        internal void BeginElse()
        {
            if (_current is null)
            {
                throw new InvalidDataException("A conditional directive cannot contain more than one else branch.");
            }

            _previous.Add(_current);
            _current = null;
        }
    }
}
