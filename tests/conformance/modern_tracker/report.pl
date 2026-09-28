% Audit helper: collect every answer or the formal error, without treating
% known failures as passing behavior. Expected outcomes are in the tracker.
tracker_report(Id, Goal, Template) :-
    catch((findall(Template, Goal, Answers), Result = solutions(Answers)),
          error(Formal, _), Result = error(Formal)),
    writeq(observation(Id, Result)), nl.
