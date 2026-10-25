# Go-Live Setup Specification

## Purpose

The two things go-live day requires and that only an Admin does: giving each pre-existing contract its
cutover date, and setting the number the first recibo will carry. The screen shows, for each, whether it
can still be changed, so that nobody discovers a lock by being refused.

## Requirements

### Requirement: The Admin Sees Each Contract's Cutover State

The go-live screen MUST list every contract (Decision 30) with its tenant and unit or units, its cutover
date or the fact that it has none, and whether the date can still be changed. The date is **changeable**
while the contract's account holds no movement, and **locked** from the first movement (`contract-account`:
"A Pre-Existing Contract Accrues Nothing Due On or Before Its Cutover Date"). A contract with no account
MUST be listed as not settable, with the reason. The screen MUST be available to an Admin only
(Requirement 2).

#### Scenario: A contract with no cutover date and no movement

- GIVEN a contract with no cutover date and an account with no movement
- WHEN the screen is shown
- THEN it MUST read "no cutover date" and changeable

#### Scenario: A contract whose date is set and unlocked

- GIVEN a cutover date of 5 October and no movement
- WHEN the screen is shown
- THEN it MUST show 5 October and changeable

#### Scenario: A locked contract

- GIVEN an account holding any movement
- WHEN the screen is shown
- THEN it MUST show the date (or none) and locked, and MUST NOT offer a change

#### Scenario: A contract with no account

- GIVEN a contract with no contract account
- WHEN the screen is shown
- THEN it MUST be listed as not settable, naming that it has no account

### Requirement: The Admin Sets a Contract's Cutover Date While It Is Still Changeable

The Admin MUST be able to set a contract's cutover date from the screen, and to correct it while the
contract's account holds no movement. The change MUST go through the contract's own domain operation
(`Contract.SetCutoverDate`, `Contract.cs:199-210`), so that the domain's rules apply: the date MUST NOT
be after the agency's today and MUST NOT be before the contract's start date (Decision 22; `contract-account`
below), and the account MUST hold no movement. The screen MUST show a refused value or a lock in operator
text (Decision 31) and MUST refresh the contract's state afterwards. There is no operation to clear a
cutover date.

#### Scenario: An Admin sets a date

- GIVEN a changeable contract and an Admin
- WHEN the Admin sets 5 October (the agency's today being 6 October or later)
- THEN the date MUST be stored and the screen MUST show 5 October

#### Scenario: A mistyped date is corrected before use

- GIVEN a date of 5 October set and no movement
- WHEN the Admin sets 6 October
- THEN it MUST be accepted

#### Scenario: A future date is refused

- GIVEN the agency's today is 20 October
- WHEN the Admin sets 21 October
- THEN it MUST be refused with operator text, and nothing MUST be stored

#### Scenario: A date before the contract's start is refused

- GIVEN a contract starting 1 March 2025
- WHEN the Admin sets 28 February 2025
- THEN it MUST be refused with operator text

#### Scenario: Today and the start date are accepted

- GIVEN a contract starting 1 March 2025 and today 20 October 2026
- WHEN the Admin sets 20 October 2026, and separately 1 March 2025
- THEN each MUST be accepted

#### Scenario: A locked contract refuses

- GIVEN an account holding a movement
- WHEN the Admin attempts to set a date, including when the screen's state is out of date
- THEN it MUST be refused with operator text saying the date is locked, and the screen MUST refresh to show locked

#### Scenario: A concurrent first movement is a refusal, not a crash

- GIVEN a contract that was changeable when the screen loaded, and a payment is then committed
- WHEN the Admin submits a date
- THEN the database's lock MUST be shown as a refusal and not as a generic error

### Requirement: The Admin Sees and Sets the Recibo Starting Number

The screen MUST show the state of the agency's recibo starting number: **not set**, **set to N and
changeable** (no recibo exists yet), or **set to N and locked** (a recibo exists, `receipt`: "The
Starting Number Is a Cutover Parameter, Set Before the First Recibo"). The Admin MUST be able to set it, and
correct it, while no recibo exists. The screen MUST refuse before calling the port anything that is not a
whole number of at least 1 and at most the largest `int` (Decision 32). The database's refusals (locked,
not Admin, below 1) MUST be shown as operator text (Decision 31). The state MUST be readable by both roles
(the cash desk's readiness depends on it, Requirement 6) while only an Admin can see the screen and set it.

#### Scenario: Not set

- GIVEN a counter whose starting number was never set
- WHEN the Admin opens the screen
- THEN it MUST read "not set" and changeable

#### Scenario: Set and changeable

- GIVEN a starting number of 31001 and no recibo
- WHEN the screen is shown
- THEN it MUST read 31001 and changeable

#### Scenario: Locked after the first recibo

- GIVEN a starting number of 31001 and one recibo issued
- WHEN the screen is shown
- THEN it MUST read 31001 and locked, and MUST NOT offer a change

#### Scenario: A mistyped number is corrected before use

- GIVEN 31001 set and no recibo
- WHEN the Admin sets 31100
- THEN it MUST be accepted, and the first recibo issued MUST carry 31100

#### Scenario: An impossible number is refused before the port

- GIVEN any state
- WHEN the Admin enters 0, -5, 12.5, text, or a number above the largest `int`
- THEN it MUST be refused with operator text and the port MUST NOT be called

#### Scenario: A locked number refused by the database is a refusal

- GIVEN a screen showing the number changeable, and a recibo is issued before the Admin submits
- WHEN the Admin submits a new number
- THEN the database's lock MUST be shown as operator text, not as a generic error

## Tests

27. **The setup list shows each contract's cutover date or none, changeable or locked, and a contract with
    no account as not settable.** *(Infrastructure.Tests for the port; Presentation.Tests for the display)*
28. **Setting a cutover date through the setup port stores it; correcting it before any movement is
    accepted; after a movement it is refused as a refusal.** *(Infrastructure.Tests)*
29. **The starting number's state is not set, set and changeable, or set and locked, readable by both
    roles.** *(Infrastructure.Tests)*
30. **A starting number of 0, -5, 12.5, text or above the largest `int` is refused before the port is
    called; 31001 then 31100 before any recibo is accepted and the first recibo carries 31100.**
    *(Presentation.Tests for the refusal before the port; Infrastructure.Tests for the rest)*
31. **A database lock that arrives after the screen showed "changeable" is shown as a refusal, for the
    cutover date and for the starting number.** *(Presentation.Tests with the port raising the mapped
    refusal)*
