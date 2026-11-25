# Presenter guide: AI writes the code. Who assesses the risks?

Use the same feature throughout the lecture. Reveal the system gradually instead of asking the audience to read a large diff. The PR is the realistic review workload; the handful of opened functions are evidence for the discussion.

## Opening: one request, many consequences (5 minutes)

Show the original request:

> Reserve the items in an order for ten minutes while payment is processed. Release them if the order is cancelled or payment fails. Show the customer what happened.

Ask: “If an agent delivers this PR with green tests, what would you need to know before approving it?” Show the file count and the changed services. Do not use lines of code as a correctness metric. Generated migrations account for part of the diff and should be identified as such.

## 1. Shape the work before generation (10 minutes)

- **Plans and specifications:** write the deadline and all-or-nothing promise. Does the deadline mean payment started, provider succeeded, or Catalog received the result?
- **Repository context and constraints:** keep Catalog as the owner of quantities; tell the agent about existing payment events and integration logs.
- **Smaller changes and boundaries:** separate persistence, lifecycle, integration, and UI review, while retaining one end-to-end feature to assess.
- **Remaining judgment:** a well-followed plan can still encode the wrong business policy. Ask who chose ten minutes and who accepts the late-payment outcome.

## 2. Produce evidence about behavior (12 minutes)

- **Tests, types, and static checks:** show the concurrent last-item test and all-or-nothing basket test. An isolated happy-path test would miss the competition.
- **Independent reviewer agents:** ask for a payment/expiry counterexample, then verify its proposed path against the implementation. An agent's explanation is a hypothesis.
- **Runtime and failure testing:** demonstrate expiry followed by late payment. Point to the durable outbox recovery test.
- **Properties and state models:** state “a release restores each held unit at most once” and “a released reservation never becomes fulfillable.” Ask which event orderings the tests actually exercise.

## 3. Build enough understanding to review (13 minutes)

- **Explanations and navigable diffs:** summarize the changed promise, then open the small amount of code that establishes it.
- **System-level views:** open the new CodeBoarding PR review. Follow WebApp → Ordering → Catalog → PaymentProcessor; expand the reservation service and outbox. Show that the payment dependency changed even though checkout still looks familiar.
- **Interactive scenarios:** run `late-payment`. Predict the customer-facing states before watching them. Then trace why payment cannot consume another customer's reserved item.
- **Shared walkthroughs:** ask “What happens if Catalog commits and crashes before publishing?” Let the audience identify the missing component before expanding the worker and outbox.

## 4. Decide what may ship (8 minutes)

- **Blast radius and reversibility:** this touches fulfillment eligibility, quantities, and a shared message bus. Rolling back just the UI cannot restore the old protocol.
- **Evidence and accountable owners:** tests support invariants. An engineer still approves the deadline, payment compensation, and consistency model.
- **Release and recovery:** explain draining old orders, deploying matching event consumers, monitoring pending outcomes, and handling payment review.
- **Learning after delivery:** record unexpected event sequences and feed them into tests, constraints, and future review guidance.

## Close (2 minutes)

Return to the opening question. The useful result of review is that someone can explain the changed promises, the affected components, the evidence, and the remaining decisions. CodeBoarding is one aid in producing that understanding.

Total: approximately 50 minutes, including the short application and CodeBoarding demonstrations. Keep questions or a longer recovery demo as a ten-minute extension.
