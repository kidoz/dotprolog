% GAP-70 requires module text in the same load unit as the meta-call.
:- module(tracker_module).
:- end_module(tracker_module).
:- body(tracker_module).
marker.
:- end_body(tracker_module).

:- initialization(main).
% Keep the literal meta-call here: passing it as data through tracker_report/3
% bypasses the source conversion that exposes internal names in the culprit.
main :-
    catch((call((write(marker),23)), Result = succeeded),
          error(Formal, _), Result = error(Formal)),
    writeq(observation(module_call_culprit, Result)), nl.
