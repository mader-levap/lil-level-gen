Current tasks:
- in `GenerationStatus` we have `Completion` field. It should relatively accurately reflect progress of generation.
  Issue is that phases of generation are very different, some of them finish in milliseconds and other can take longer, especially map generation itself.
  So I guess we need to measure actual progress and convert it to percentage using weights or something...
  Note that map generation is simulated for now, so progress there is simulated too.
