#include <rex/thread/fiber.h>

#include <cstdio>
#include <thread>

using rex::thread::Fiber;

int main() {
  auto* current = Fiber::ConvertCurrentThread();
  Fiber* finished_worker = nullptr;
  std::thread([&] { finished_worker = Fiber::ConvertCurrentThread(); }).join();

  // XThread can be released by another guest thread after its worker exits.
  finished_worker->Destroy();
  if (Fiber::Current() != current) {
    std::fputs("FAIL: worker cleanup cleared the loading thread's fiber\n", stderr);
    return 1;
  }

  struct State {
    Fiber* main;
    int resumes = 0;
  } state{current};
  auto* child = Fiber::Create(256 * 1024, [](void* arg) {
    auto& state = *static_cast<State*>(arg);
    for (;;) {
      ++state.resumes;
      Fiber::SwitchTo(state.main);
    }
  }, &state);
  for (int i = 1; i <= 1000; ++i) {
    Fiber::SwitchTo(child);
    if (state.resumes != i || Fiber::Current() != current) return 2;
  }
  child->Destroy();
  if (Fiber::Current() != current) return 3;
  current->Destroy();
  if (Fiber::Current() != nullptr) return 4;
  std::puts("PASS: worker cleanup and 1000 fiber round trips");
}
