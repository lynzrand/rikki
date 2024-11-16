# Merge queue design

A merge queue is a list of pull requests to be checked by CI before merging into the target branch. The PRs are added in order into the same branch, and merged in order.

By queue conventions, the oldest commit is called the head commit, and the newest is called the tail.

## Adding a PR to merge queue

A PR can be added into a merge queue when:

- It has been requested to be added to a merge queue
- It has passed its own merge CI
- It has no merge conflict with the target branch

The PR should be inserted after the last item where `other.priority >= pr.priority`. In most cases this means the PR is added to the tail of the queue; but if not, the merge queue needs to be rebuilt.

A CI should be triggered for the merge commit (or tail commit if it uses rebase).

If the CI for the given PR is still running, it should be queued for insertion, but not inserted yet.

## Popping PRs from the merge queue

If CI passed for the head PR, it's popped from the merge queue, and the target branch is fast forwarded to it. Subsequent PRs that passed CI before this one will also be merged.

## Rebuilding the merge queue

A merge queue should be rebuilt on one of the following cases:

- An enqueued PR has failed CI; the failed PR should be removed from queue.
- A PR is inserted in the middle of the queue.
- Code was pushed onto the target branch, or onto one of the enqueued PRs.

On any case, the portion of the queue after the affected point need to be torn down (including CI abort), and rebuilt in order. CI needs to be re-triggered for every PR in this queue.
