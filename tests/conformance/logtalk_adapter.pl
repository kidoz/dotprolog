:- dynamic('$logtalk_counter'/1).
:- dynamic('$logtalk_fixture_path'/2).
:- dynamic('$logtalk_named_input_path'/2).

'$logtalk_counter'(0).
'$logtalk_current_flag'(prolog_dialect, dotprolog).
'$logtalk_is_windows' :- fail.

'$logtalk_member'(Item, [Item|_]).
'$logtalk_member'(Item, [_|Rest]) :-
    '$logtalk_member'(Item, Rest).

forall(Condition, Action) :-
    \+ (call(Condition), \+ call(Action)).

memberchk(Item, List) :-
    '$logtalk_member'(Item, List),
    !.

'$logtalk_new_path'(Prefix, Path) :-
    '$logtalk_root'(Root),
    retract('$logtalk_counter'(Count)),
    Next is Count + 1,
    assertz('$logtalk_counter'(Next)),
    number_chars(Next, Digits),
    atom_chars(Number, Digits),
    atom_concat(Root, '/', WithSlash),
    atom_concat(WithSlash, Prefix, WithPrefix),
    atom_concat(WithPrefix, '-', WithDash),
    atom_concat(WithDash, Number, Path).

'$logtalk_text_atom'(Text, Atom) :-
    atom(Text),
    !,
    Atom = Text.
'$logtalk_text_atom'(Text, Atom) :-
    '$logtalk_text_atom_parts'(Text, Atom).

'$logtalk_text_atom_parts'([], '').
'$logtalk_text_atom_parts'([Head|Tail], Atom) :-
    atom(Head),
    '$logtalk_text_atom_parts'(Tail, Rest),
    atom_concat(Head, Rest, Atom).

'$logtalk_write_text'(Path, Text) :-
    '$logtalk_text_atom'(Text, Atom),
    open(Path, write, Output),
    write(Output, Atom),
    close(Output).

'$logtalk_read_codes'(Input, Codes) :-
    get_code(Input, Code),
    (   Code == -1
    ->  Codes = []
    ;   Codes = [Code|Rest],
        '$logtalk_read_codes'(Input, Rest)
    ).

'$logtalk_read_atom'(Path, Atom) :-
    open(Path, read, Input),
    '$logtalk_read_codes'(Input, Codes),
    close(Input),
    atom_codes(Atom, Codes).

'$logtalk_read_chars'(Input, Chars) :-
    get_char(Input, Char),
    (   Char == end_of_file
    ->  Chars = []
    ;   Chars = [Char|Rest],
        '$logtalk_read_chars'(Input, Rest)
    ).

'$logtalk_read_character_list'(Path, Chars) :-
    open(Path, read, Input),
    '$logtalk_read_chars'(Input, Chars),
    close(Input).

'$logtalk_set_text_input'(Text) :-
    '$logtalk_new_path'(input, Path),
    '$logtalk_write_text'(Path, Text),
    open(Path, read, Input),
    set_input(Input).

'$logtalk_set_text_input'(Alias, Text) :-
    '$logtalk_new_path'(input, Path),
    '$logtalk_write_text'(Path, Text),
    open(Path, read, Input, [alias(Alias)]),
    assertz('$logtalk_named_input_path'(Alias, Path)).

'$logtalk_delete_text_input'(Alias) :-
    retract('$logtalk_named_input_path'(Alias, _)).

'$logtalk_set_text_output'(Initial) :-
    '$logtalk_new_path'(output, Path),
    open(Path, write, Output),
    write(Output, Initial),
    set_output(Output).

'$logtalk_set_text_output'(Alias, Initial) :-
    '$logtalk_new_path'(output, Path),
    open(Path, write, Output, [alias(Alias)]),
    write(Output, Initial).

'$logtalk_take_current_text_output'(Path) :-
    current_output(Output),
    stream_property(Output, file_name(Path)),
    close(Output).

'$logtalk_take_named_text_output'(Alias, Path) :-
    stream_property(Alias, file_name(Path)),
    close(Alias).

'$logtalk_text_output_assertion'(Expected, Assertion) :-
    '$logtalk_take_current_text_output'(Path),
    '$logtalk_read_atom'(Path, Actual),
    Assertion = (Expected == Actual).

'$logtalk_text_output_assertion'(Alias, Expected, Assertion) :-
    '$logtalk_take_named_text_output'(Alias, Path),
    '$logtalk_read_atom'(Path, Actual),
    Assertion = (Expected == Actual).

'$logtalk_text_output_contents'(Contents) :-
    '$logtalk_take_current_text_output'(Path),
    '$logtalk_read_character_list'(Path, Contents).

'$logtalk_text_output_contents'(Alias, Contents) :-
    '$logtalk_take_named_text_output'(Alias, Path),
    '$logtalk_read_character_list'(Path, Contents).

'$logtalk_check_text_output'(Alias, Expected) :-
    '$logtalk_take_named_text_output'(Alias, Path),
    '$logtalk_read_atom'(Path, ActualAtom),
    '$logtalk_text_atom'(Expected, ExpectedAtom),
    ActualAtom == ExpectedAtom.

'$logtalk_file_path'(Fixture, Path) :-
    '$logtalk_fixture_path'(Fixture, Existing),
    !,
    Path = Existing.
'$logtalk_file_path'(Fixture, Path) :-
    '$logtalk_new_path'(fixture, Path),
    assertz('$logtalk_fixture_path'(Fixture, Path)).

'$logtalk_create_text_file'(Path, Contents) :-
    '$logtalk_write_text'(Path, Contents).

'$logtalk_write_bytes'(_, []).
'$logtalk_write_bytes'(Output, [Byte|Rest]) :-
    put_byte(Output, Byte),
    '$logtalk_write_bytes'(Output, Rest).

'$logtalk_create_binary_file'(Path, Contents) :-
    open(Path, write, Output, [type(binary)]),
    '$logtalk_write_bytes'(Output, Contents),
    close(Output).

'$logtalk_closed_input_stream'(Stream, []) :-
    '$logtalk_new_path'(closed, Path),
    open(Path, write, Output),
    close(Output),
    open(Path, read, Stream),
    close(Stream).

'$logtalk_closed_output_stream'(Stream, []) :-
    '$logtalk_new_path'(closed, Path),
    open(Path, write, Stream),
    close(Stream).

'$logtalk_open_binary_output'(Path, Alias, Initial, Stream) :-
    '$logtalk_create_binary_file'(Path, Initial),
    (   Alias == []
    ->  open(Path, append, Stream, [type(binary)])
    ;   open(Path, append, Stream, [type(binary), alias(Alias)])
    ).

'$logtalk_create_binary_output'(Initial, Stream) :-
    '$logtalk_new_path'(binary, Path),
    '$logtalk_open_binary_output'(Path, [], Initial, Stream).

'$logtalk_set_named_binary_output'(Alias, Initial) :-
    '$logtalk_new_path'(binary, Path),
    '$logtalk_open_binary_output'(Path, Alias, Initial, _).

'$logtalk_read_bytes'(Input, Bytes) :-
    get_byte(Input, Byte),
    (   Byte == -1
    ->  Bytes = []
    ;   Bytes = [Byte|Rest],
        '$logtalk_read_bytes'(Input, Rest)
    ).

'$logtalk_binary_output_assertion'(Expected, Assertion) :-
    current_output(Output),
    stream_property(Output, file_name(Path)),
    close(Output),
    open(Path, read, Input, [type(binary)]),
    '$logtalk_read_bytes'(Input, Actual),
    close(Input),
    Assertion = (Expected == Actual).

'$logtalk_binary_output_assertion'(Alias, Expected, Assertion) :-
    stream_property(Alias, file_name(Path)),
    close(Alias),
    open(Path, read, Input, [type(binary)]),
    '$logtalk_read_bytes'(Input, Actual),
    close(Input),
    Assertion = (Expected == Actual).

'$logtalk_suppress_text_output' :-
    '$logtalk_new_path'(suppressed, Path),
    open(Path, write, Output),
    set_output(Output).
