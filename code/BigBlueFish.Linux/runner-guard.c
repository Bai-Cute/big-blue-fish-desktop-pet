#include <sys/prctl.h>
#include <signal.h>
#include <unistd.h>
#include <stdio.h>
int main(int argc, char **argv) {
    if (argc < 2) return 64;
    pid_t parent = getppid();
    if (prctl(PR_SET_PDEATHSIG, SIGKILL) != 0 || getppid() != parent) return 70;
    execvp(argv[1], argv + 1);
    perror("BigBlueFish runner");
    return 71;
}
