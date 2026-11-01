# 05: AI backend, Organize and the capture strip

**What to build:** After saving, the AI suggests a Project, People and a title, and writes an English card, in the background. Results appear in the capture strip. The owner sets the API key through `dotnet user-secrets` in development and Credential Manager in the app, and never pastes it in chat. Until ticket 06 exists, checks stay small and the owner may set a spending limit on the API account.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] The model backend interface has an API key implementation, with Sonnet 5.5 as the Organize default
- [ ] Capture never waits: the strip fills in a few seconds later
- [ ] Project goes to Unsorted when nothing fits, and a new Project needs the person's confirmation
- [ ] A Keep-local Thought makes no AI call and is left out of other Thoughts' context, confirmed by logging what is sent
- [ ] Cards are in `.thoughts/`, and the AI writes neither `status` nor the body
- [ ] Note text in a prompt is treated as data: a fake note containing instructions doesn't change behaviour
- [ ] A failed call leaves the Thought saved, and it retries later
- [ ] Dutch, English and typos each get a sensible result on fake notes
- [ ] Verify on the throwaway vault with the real AI and report the AI cost of the check.
